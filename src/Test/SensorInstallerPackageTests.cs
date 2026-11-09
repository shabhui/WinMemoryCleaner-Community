using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class SensorInstallerPackageTests
    {
        private string _root, _cache, _hash;
        private byte[] _data;
        private int _downloads, _signatures;
        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Environment.GetEnvironmentVariable("PI_SCRATCH_DIR") ?? TestContext.CurrentContext.WorkDirectory, "optional-installer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root); _cache = Path.Combine(_root, "cache");
            _data = Encoding.UTF8.GetBytes("test bytes, never an executable");
            using (var stream = new MemoryStream(_data, false)) _hash = SensorPayload.Hash(stream);
            _downloads = 0; _signatures = 0;
        }
        [TearDown]
        public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        private void Download(Stream target, CancellationToken cancellation)
        { Interlocked.Increment(ref _downloads); using(var input=new MemoryStream(_data, false)) SensorInstallerPackage.CopyPinned(input, target, _data.Length, cancellation); }
        private bool Verify(string path)
        {
            Interlocked.Increment(ref _signatures);
            Assert.Throws<IOException>(delegate { File.WriteAllText(path, "replace"); });
            Assert.Throws<IOException>(delegate { File.Delete(path); });
            return true;
        }
        private SensorInstallerPackage Package()
        { return new SensorInstallerPackage(_cache, _data.Length, _hash, Download, Verify); }
        private string LocalFile(byte[] bytes)
        { string path = Path.Combine(_root, "offline.exe"); File.WriteAllBytes(path, bytes); return path; }
        private void AssertNoPublishedFile()
        {
            string directory = Path.Combine(_cache, _hash);
            if (Directory.Exists(directory)) Assert.AreEqual(0, Directory.GetFiles(directory).Length, "No partial or rejected installer may be published.");
        }
        [Test]
        public void VerifiedDownloadIsCachedAndHeldUntilLeaseDisposal()
        {
            var package = Package();
            using (SensorPayloadLease lease = package.Prepare(null, CancellationToken.None))
            {
                Assert.AreEqual(Path.GetFullPath(Path.Combine(_cache, _hash)), lease.Directory);
                Assert.AreEqual(Path.Combine(lease.Directory, "PawnIO_setup.exe"), lease.Installer);
                CollectionAssert.AreEqual(_data, File.ReadAllBytes(lease.Installer));
                Assert.Throws<IOException>(delegate { File.WriteAllText(lease.Installer, "replacement"); });
                Assert.Throws<IOException>(delegate { Directory.Move(lease.Directory, lease.Directory + ".old"); });
            }
            using (SensorPayloadLease lease = package.Prepare(null, CancellationToken.None)) Assert.IsTrue(File.Exists(lease.Installer));
            Assert.AreEqual(1, _downloads); Assert.GreaterOrEqual(_signatures, 2);
        }
        [Test]
        public void LocalPackageIsCopiedVerifiedAndNeverDownloads()
        {
            string path = LocalFile(_data);
            using (SensorPayloadLease lease = Package().Prepare(path, CancellationToken.None))
            { Assert.AreNotEqual(path, lease.Installer); CollectionAssert.AreEqual(_data, File.ReadAllBytes(lease.Installer)); }
            Assert.AreEqual(0, _downloads); CollectionAssert.AreEqual(_data, File.ReadAllBytes(path));
        }
        [TestCase("")]
        [TestCase("relative.exe")]
        [TestCase("\\\\server\\share\\offline.exe")]
        [TestCase("C:relative.exe")]
        [TestCase("\\root-relative.exe")]
        [TestCase("//server/share/offline.exe")]
        public void InvalidLocalPathCannotSilentlyRequestADownload(string path)
        {
            Assert.Throws<InvalidDataException>(delegate { Package().Prepare(path, CancellationToken.None); });
            Assert.AreEqual(0, _downloads); AssertNoPublishedFile(); Assert.IsFalse(Directory.Exists(_cache), "Invalid paths must be rejected before cache or source I/O.");
        }
        [TestCase(-1)]
        [TestCase(1)]
        public void TruncatedOrOversizedDownloadNeverReachesSignatureVerification(int difference)
        {
            var package = new SensorInstallerPackage(_cache, _data.Length, _hash, delegate(Stream target, CancellationToken cancellation)
            { target.Write(new byte[_data.Length + difference], 0, _data.Length + difference); }, Verify);
            Assert.Throws<InvalidDataException>(delegate { package.Prepare(null, CancellationToken.None); });
            Assert.AreEqual(0, _signatures); AssertNoPublishedFile();
        }
        [Test]
        public void IncorrectHashIsRejectedBeforeSignatureVerification()
        {
            var package = new SensorInstallerPackage(_cache, _data.Length, new string('0', 64), Download, Verify);
            Assert.Throws<InvalidDataException>(delegate { package.Prepare(null, CancellationToken.None); });
            Assert.AreEqual(0, _signatures);
            string directory = Path.Combine(_cache, new string('0', 64));
            if (Directory.Exists(directory)) Assert.AreEqual(0, Directory.GetFiles(directory).Length);
        }
        [Test]
        public void InvalidSignatureNeverPublishesAnInstaller()
        {
            var package = new SensorInstallerPackage(_cache, _data.Length, _hash, Download, delegate { return false; });
            Assert.Throws<InvalidDataException>(delegate { package.Prepare(null, CancellationToken.None); }); AssertNoPublishedFile();
        }
        [Test]
        public void CorruptedCacheMustBeReacquiredAndReverified()
        {
            var package = Package(); string path;
            using (SensorPayloadLease lease = package.Prepare(null, CancellationToken.None)) path = lease.Installer;
            File.WriteAllText(path, "corrupted");
            using (SensorPayloadLease lease = package.Prepare(null, CancellationToken.None)) CollectionAssert.AreEqual(_data, File.ReadAllBytes(lease.Installer));
            Assert.AreEqual(2, _downloads);
        }
        [Test]
        public void InvalidLocalSelectionCannotUseNetworkOrHideBehindAValidCache()
        {
            var package = Package();
            using (SensorPayloadLease lease = package.Prepare(null, CancellationToken.None)) Assert.IsNotNull(lease);
            Assert.Throws<InvalidDataException>(delegate { package.Prepare(LocalFile(new byte[_data.Length]), CancellationToken.None); });
            Assert.AreEqual(1, _downloads);
            using (SensorPayloadLease lease = package.Prepare(null, CancellationToken.None)) CollectionAssert.AreEqual(_data, File.ReadAllBytes(lease.Installer));
            Assert.AreEqual(1, _downloads);
        }
        [Test]
        public void CancellationBeforePreparationDoesNotReadOrDownload()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(delegate { Package().Prepare(null, cancellation.Token); });
            }
            Assert.AreEqual(0, _downloads); Assert.AreEqual(0, _signatures); Assert.IsFalse(Directory.Exists(_cache));
        }
        [Test]
        public void CancellationDuringAcquisitionRemovesItsPartialFile()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var package = new SensorInstallerPackage(_cache, _data.Length, _hash, delegate(Stream target, CancellationToken token)
                { target.Write(_data, 0, _data.Length); cancellation.Cancel(); }, Verify);
                Assert.Throws<OperationCanceledException>(delegate { package.Prepare(null, cancellation.Token); });
            }
            Assert.AreEqual(0, _signatures); AssertNoPublishedFile();
        }
        [Test]
        public void ConcurrentPreparationDownloadsOnlyOnce()
        {
            var package = Package(); var errors = new Exception[2]; var threads = new Thread[2];
            for (int i = 0; i < threads.Length; i++)
            {
                int index = i;
                threads[i] = new Thread(delegate()
                { try { using (SensorPayloadLease lease = package.Prepare(null, CancellationToken.None)) Assert.IsNotNull(lease); } catch (Exception error) { errors[index] = error; } });
                threads[i].Start();
            }
            foreach (Thread thread in threads) Assert.IsTrue(thread.Join(10000));
            foreach (Exception error in errors) Assert.IsNull(error);
            Assert.AreEqual(1, _downloads);
        }
        [TestCase(-1)]
        [TestCase(1)]
        public void BoundedCopyRejectsWrongLengthWithoutWritingPastThePin(int difference)
        {
            using (var input = new MemoryStream(new byte[_data.Length + difference], false))
            using (var output = new MemoryStream())
            {
                Assert.Throws<InvalidDataException>(delegate { SensorInstallerPackage.CopyPinned(input, output, _data.Length, CancellationToken.None); });
                Assert.LessOrEqual(output.Length, _data.Length);
            }
        }
        [Test]
        public void RequestUsesFixedHttpsSourceWithoutAutomaticRedirectsOrCredentials()
        {
            HttpWebRequest request = SensorInstallerPackage.CreateRequest();
            Assert.AreEqual(SensorInstallerPackage.DownloadUrl, request.RequestUri.AbsoluteUri);
            Assert.AreEqual("https", request.RequestUri.Scheme); Assert.IsFalse(request.AllowAutoRedirect);
            Assert.IsFalse(request.UseDefaultCredentials); Assert.IsNull(request.Credentials);
            Assert.Greater(request.Timeout, 0); Assert.LessOrEqual(request.Timeout, 15000);
            Assert.Greater(request.ReadWriteTimeout, 0); Assert.LessOrEqual(request.ReadWriteTimeout, 15000);
            Assert.AreEqual("application/octet-stream", request.Accept);
        }
        [Test]
        public void OnlyBoundedOfficialReleaseAssetRedirectsAreAllowed()
        {
            var current = new Uri(SensorInstallerPackage.DownloadUrl);
            const string asset = "https://release-assets.githubusercontent.com/github-production-release-asset/verified?token=public";
            for (int redirects = 0; redirects < 3; redirects++)
                Assert.AreEqual(asset, SensorInstallerPackage.ResolveRedirect(current, asset, redirects).AbsoluteUri);
            Assert.Throws<InvalidDataException>(delegate { SensorInstallerPackage.ResolveRedirect(current, asset, 3); });
            Assert.Throws<InvalidDataException>(delegate { SensorInstallerPackage.ResolveRedirect(current, asset, -1); });
            Assert.Throws<InvalidDataException>(delegate { SensorInstallerPackage.ResolveRedirect(null, asset, 0); });
        }
        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("http://release-assets.githubusercontent.com/file")]
        [TestCase("https://release-assets.githubusercontent.com.evil.invalid/file")]
        [TestCase("https://evil.invalid/file")]
        [TestCase("https://github.com/another/repository/file")]
        [TestCase("https://github.com/namazso/PawnIO.Setup/releases/latest")]
        [TestCase("https://release-assets.githubusercontent.com:444/file")]
        [TestCase("https://name:password@release-assets.githubusercontent.com/file")]
        [TestCase("https://release-assets.githubusercontent.com/file#fragment")]
        [TestCase("file:///C:/installer.exe")]
        public void UntrustedDownloadRedirectCannotBeFollowed(string location)
        { Assert.Throws<InvalidDataException>(delegate { SensorInstallerPackage.ResolveRedirect(new Uri(SensorInstallerPackage.DownloadUrl), location, 0); }); }
    }
}
#pragma warning restore 1591
