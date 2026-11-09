using System;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text.RegularExpressions;
using System.Threading;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    /// <summary>Acquires the pinned optional installer only after explicit user confirmation.</summary>
    internal sealed class SensorInstallerPackage
    {
        internal const long InstallerBytes = SensorDriverTrust.OfficialInstallerBytes;
        internal const string InstallerHash = SensorDriverTrust.OfficialInstallerHash;
        internal const string DownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
        private readonly string _root, _directory, _hash;
        private readonly long _length;
        private readonly Action<Stream, CancellationToken> _download;
        private readonly Func<string, bool> _verify;

        internal SensorInstallerPackage(string root, long length, string hash, Action<Stream, CancellationToken> download, Func<string, bool> verify)
        {
            if (String.IsNullOrEmpty(root) || download == null || verify == null) throw new ArgumentException("Missing installer package input.");
            if (length <= 0 || length > 67108864 || hash == null || !Regex.IsMatch(hash, @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid installer pin.");
            _root = Path.GetFullPath(root);
            if (_root.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidDataException("Installer cache must be on a local path.");
            _directory = Path.Combine(_root, hash); _hash = hash; _length = length; _download = download; _verify = verify;
        }
        internal static SensorInstallerPackage CreateDefault()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"WinMemoryCleaner.Community\SensorInstallers");
            return new SensorInstallerPackage(root, InstallerBytes, InstallerHash, DownloadOfficial, SensorDriverTrust.VerifyInstaller);
        }
        internal SensorPayloadLease Prepare(string localPath, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            // Null explicitly selects download/cache. Empty, relative and network paths never fall back to a download.
            if (localPath != null && !Regex.IsMatch(localPath, @"\A[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)) throw new InvalidDataException("Select a fully qualified local official installer file.");
            using (var mutex = new Mutex(false, @"Local\WinMemoryCleaner.SensorInstaller." + _hash))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(5000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Installer package preparation is busy.");
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    SensorPayload.CheckDirectoryChain(_root); Directory.CreateDirectory(_root);
                    using (var rootLease = new SensorPayloadLease(_root))
                    {
                        rootLease.LockDirectories();
                        SensorPayload.CheckDirectoryChain(_directory); Directory.CreateDirectory(_directory);
                        using (var directoryLease = new SensorPayloadLease(_directory))
                        {
                            directoryLease.LockDirectories();
                            return PrepareLocked(localPath, cancellation);
                        }
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        private SensorPayloadLease PrepareLocked(string localPath, CancellationToken cancellation)
        {
            string destination = Path.Combine(_directory, "PawnIO_setup.exe");
            if (localPath == null)
            {
                SensorPayloadLease cached = OpenVerified(destination);
                if (cached != null)
                {
                    if (cancellation.IsCancellationRequested) { cached.Dispose(); cancellation.ThrowIfCancellationRequested(); }
                    return cached;
                }
            }
            string temporary = Path.Combine(_directory, ".prepare-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    if (localPath == null) _download(output, cancellation);
                    else
                    {
                        string source = Path.GetFullPath(localPath);
                        using (var localLease = new SensorPayloadLease(Path.GetDirectoryName(source)))
                        {
                            localLease.LockDirectories();
                            FileStream input = localLease.LockFile(source);
                            if (input.Length != _length) throw new InvalidDataException("Official installer length mismatch.");
                            CopyPinned(input, output, _length, cancellation);
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    output.Position = 0;
                    if (output.Length != _length || !String.Equals(SensorPayload.Hash(output), _hash, StringComparison.Ordinal)) throw new InvalidDataException("Official installer hash/length mismatch.");
                }
                // Verify while locked, then verify again after publication. Staging bytes are never executable input.
                using (SensorPayloadLease staged = OpenVerified(temporary))
                    if (staged == null) throw new InvalidDataException("Official installer signature rejected.");
                cancellation.ThrowIfCancellationRequested();
                if (File.Exists(destination))
                {
                    if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installer cache contains a reparse point.");
                    File.Delete(destination);
                }
                File.Move(temporary, destination);
                SensorPayloadLease ready = OpenVerified(destination);
                if (ready == null) throw new InvalidDataException("Prepared installer failed verification.");
                if (cancellation.IsCancellationRequested) { ready.Dispose(); cancellation.ThrowIfCancellationRequested(); }
                return ready;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private SensorPayloadLease OpenVerified(string path)
        {
            if (!File.Exists(path)) return null;
            SensorPayloadLease lease = null;
            try
            {
                lease = new SensorPayloadLease(_directory); lease.LockDirectories();
                FileStream input = lease.LockFile(path);
                if (input.Length != _length || !String.Equals(SensorPayload.Hash(input), _hash, StringComparison.Ordinal) || !_verify(path)) return null;
                SensorPayloadLease verified = lease; lease = null; return verified;
            }
            finally { if (lease != null) lease.Dispose(); }
        }
        internal static void CopyPinned(Stream input, Stream output, long length, CancellationToken cancellation)
        {
            var buffer = new byte[8192]; long copied = 0;
            while (copied < length)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length - copied));
                if (count == 0) throw new InvalidDataException("Official installer download is incomplete.");
                output.Write(buffer, 0, count); copied += count;
            }
            cancellation.ThrowIfCancellationRequested();
            if (input.ReadByte() != -1) throw new InvalidDataException("Official installer exceeds the pinned length.");
        }
        internal static HttpWebRequest CreateRequest()
        { return CreateRequest(new Uri(DownloadUrl, UriKind.Absolute)); }
        private static HttpWebRequest CreateRequest(Uri uri)
        {
            if (!IsAllowedDownloadUri(uri)) throw new InvalidDataException("Untrusted official installer endpoint.");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET"; request.AllowAutoRedirect = false;
            request.UseDefaultCredentials = false; request.Credentials = null;
            request.UserAgent = "WinMemoryCleaner-Community";
            request.Accept = "application/octet-stream";
            request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            return request;
        }
        private static bool IsAllowedDownloadUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment)) return false;
            return String.Equals(uri.AbsoluteUri, DownloadUrl, StringComparison.Ordinal)
                || String.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }
        internal static Uri ResolveRedirect(Uri current, string location, int redirects)
        {
            Uri next, redirect;
            if (redirects < 0 || redirects >= 3 || !IsAllowedDownloadUri(current) || String.IsNullOrEmpty(location) || !Uri.TryCreate(location, UriKind.RelativeOrAbsolute, out redirect) || !Uri.TryCreate(current, redirect, out next) || !IsAllowedDownloadUri(next))
                throw new InvalidDataException("Untrusted or excessive official installer redirect.");
            return next;
        }
        private static void Abort(HttpWebRequest request)
        { try { request.Abort(); } catch (ObjectDisposedException) { } }
        private static void DownloadOfficial(Stream output, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            // The host still targets net40. Use the same TLS 1.2 flag as its existing updater, never bypass certificates.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = CreateRequest(); int timedOut = 0;
            using (var deadline = new Timer(delegate { Interlocked.Exchange(ref timedOut, 1); Abort(Interlocked.CompareExchange(ref request, null, null)); }, null, 60000, Timeout.Infinite))
            using (cancellation.Register(delegate { Abort(Interlocked.CompareExchange(ref request, null, null)); }))
            {
                try
                {
                    int redirects = 0;
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (Interlocked.CompareExchange(ref timedOut, 0, 0) != 0) throw new TimeoutException("Official installer download timed out.");
                        HttpWebRequest active = Interlocked.CompareExchange(ref request, null, null);
                        using (var response = (HttpWebResponse)active.GetResponse())
                        {
                            if (response.ResponseUri != active.RequestUri) throw new InvalidDataException("Unexpected official installer response endpoint.");
                            int status = (int)response.StatusCode;
                            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                            {
                                Uri next = ResolveRedirect(active.RequestUri, response.Headers[HttpResponseHeader.Location], redirects++);
                                Interlocked.Exchange(ref request, CreateRequest(next));
                                continue;
                            }
                            if (response.StatusCode != HttpStatusCode.OK || (response.ContentLength >= 0 && response.ContentLength != InstallerBytes)) throw new InvalidDataException("Unexpected response for the pinned official installer.");
                            using (Stream input = response.GetResponseStream()) CopyPinned(input, output, InstallerBytes, cancellation);
                        }
                        break;
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (Interlocked.CompareExchange(ref timedOut, 0, 0) != 0) throw new TimeoutException("Official installer download timed out.");
                }
                catch (Exception error)
                {
                    var networkError = error as WebException;
                    if (networkError != null && networkError.Response != null) networkError.Response.Close();
                    cancellation.ThrowIfCancellationRequested();
                    if (Interlocked.CompareExchange(ref timedOut, 0, 0) != 0 || (networkError != null && networkError.Status == WebExceptionStatus.Timeout)) throw new TimeoutException("Official installer download timed out.", error);
                    throw;
                }
            }
        }
    }
}
