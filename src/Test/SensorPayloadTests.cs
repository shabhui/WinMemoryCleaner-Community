using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class SensorPayloadTests
    {
        private string _root;
        private Dictionary<string,byte[]> _files;
        [SetUp]
        public void Setup()
        {
            string work=Environment.GetEnvironmentVariable("PI_SCRATCH_DIR")??TestContext.CurrentContext.WorkDirectory;
            _root=Path.Combine(work,"payload-tests-"+Guid.NewGuid().ToString("N"));
            _files=new Dictionary<string,byte[]>(StringComparer.Ordinal) {
                {"WinMemoryCleaner.Sensors.exe",Encoding.UTF8.GetBytes("not an executable")},
                {"WinMemoryCleaner.Sensors.exe.config",Encoding.UTF8.GetBytes("configuration")},
                {"LibreHardwareMonitorLib.dll",Encoding.UTF8.GetBytes("not a library")},
                {"PawnIO_setup.exe",Encoding.UTF8.GetBytes("not an installer")} };
        }
        [TearDown]
        public void Cleanup() { if(Directory.Exists(_root))Directory.Delete(_root,true); }
        private static string Hash(byte[] data)
        {
            using(var hash=SHA256.Create())
            { var text=new StringBuilder();foreach(byte value in hash.ComputeHash(data))text.Append(value.ToString("x2",CultureInfo.InvariantCulture));return text.ToString(); }
        }
        private byte[] Manifest(string pathOverride,long? lengthOverride,string hashOverride)
        {
            var files=new List<object>();int index=0;
            foreach(var pair in _files)
            {
                string path=index==0&&pathOverride!=null?pathOverride:pair.Key;
                files.Add(new Dictionary<string,object>{{"path",path},{"length",index==0&&lengthOverride.HasValue?lengthOverride.Value:pair.Value.Length},{"sha256",index==0&&hashOverride!=null?hashOverride:Hash(pair.Value)},{"resource","WinMemoryCleaner.Sensors.Payload."+path+".gz"}});index++;
            }
            return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"version",SensorProtocol.PayloadVersion},{"protocol",1},{"files",files}}));
        }
        private Stream Resource(string name)
        {
            foreach(var pair in _files)
                if(name=="WinMemoryCleaner.Sensors.Payload."+pair.Key+".gz")
                {
                    var output=new MemoryStream();using(var gzip=new GZipStream(output,CompressionMode.Compress,true))gzip.Write(pair.Value,0,pair.Value.Length);
                    output.Position=0;return output;
                }
            return null;
        }
        private SensorPayload Payload() { return new SensorPayload(_root,Manifest(null,null,null),Resource); }
        [Test]
        public void PreparesExactManifestFilesAtomically()
        {
            using(var lease=Payload().Prepare())
            {
                Assert.IsNotNull(lease);Assert.IsTrue(File.Exists(lease.Executable));Assert.IsTrue(File.Exists(lease.Installer));
                Assert.AreEqual(_files.Count,Directory.GetFiles(lease.Directory).Length);Assert.AreEqual(0,Directory.GetDirectories(lease.Directory).Length);
                foreach(var pair in _files)CollectionAssert.AreEqual(pair.Value,File.ReadAllBytes(Path.Combine(lease.Directory,pair.Key)));
            }
        }
        [TestCase("../outside.exe")]
        [TestCase("..\\outside.exe")]
        [TestCase("C:\\outside.exe")]
        [TestCase("/outside.exe")]
        [TestCase("a:stream")]
        [TestCase("CON")]
        [TestCase("file.")]
        public void ManifestCannotEscapeOrNameDevicePaths(string path)
        { Assert.Throws<InvalidDataException>(delegate { new SensorPayload(_root,Manifest(path,null,null),Resource).Prepare(); }); }
        [Test]
        public void WrongHashOrLengthDoesNotPublishPartialPayload()
        {
            Assert.Throws<InvalidDataException>(delegate { new SensorPayload(_root,Manifest(null,null,new string('0',64)),Resource).Prepare(); });
            Assert.Throws<InvalidDataException>(delegate { new SensorPayload(_root,Manifest(null,999,null),Resource).Prepare(); });
            if(Directory.Exists(_root))Assert.AreEqual(0,Directory.GetDirectories(_root).Length);
        }
        [Test]
        public void MissingResourceIsRejectedBeforeExecution()
        { Assert.Throws<InvalidDataException>(delegate { new SensorPayload(_root,Manifest(null,null,null),delegate { return null; }).Prepare(); }); }
        [Test]
        public void LeasePreventsWriteFileReplacementAndDirectoryRename()
        {
            using(var lease=Payload().Prepare())
            {
                Assert.IsNotNull(lease);
                Assert.Throws<IOException>(delegate { File.WriteAllText(lease.Executable,"replacement"); });
                Assert.Throws<IOException>(delegate { File.Move(lease.Executable,lease.Executable+".old"); });
                Assert.Throws<IOException>(delegate { Directory.Move(lease.Directory,lease.Directory+".old"); });
            }
        }
        [Test]
        public void CorruptedAndUnlistedCacheFilesAreRebuiltFromEmbeddedBytes()
        {
            var payload=Payload();string folder;
            using(var lease=payload.Prepare()) { Assert.IsNotNull(lease);folder=lease.Directory; }
            File.WriteAllText(Path.Combine(folder,"LibreHardwareMonitorLib.dll"),"corrupted");File.WriteAllText(Path.Combine(folder,"unexpected.dll"),"not manifest content");
            using(var lease=payload.Prepare())
            { Assert.IsNotNull(lease);Assert.AreEqual(_files.Count,Directory.GetFiles(lease.Directory).Length);CollectionAssert.AreEqual(_files["LibreHardwareMonitorLib.dll"],File.ReadAllBytes(Path.Combine(lease.Directory,"LibreHardwareMonitorLib.dll"))); }
        }
        [Test]
        public void ConcurrentPreparationReturnsOneCompleteVersion()
        {
            var payload=Payload();var paths=new string[2];var errors=new Exception[2];var threads=new Thread[2];
            for(int i=0;i<2;i++) { int index=i;threads[i]=new Thread(delegate() { try { using(var lease=payload.Prepare()) { Assert.IsNotNull(lease);paths[index]=lease.Directory; } }catch(Exception ex){errors[index]=ex;} });threads[i].Start(); }
            foreach(var thread in threads)Assert.IsTrue(thread.Join(10000));
            foreach(var error in errors)Assert.IsNull(error);Assert.IsNotNull(paths[0]);Assert.AreEqual(paths[0],paths[1]);
        }
        [Test]
        public void CachedDirectoryJunctionIsRefusedWithoutTouchingTarget()
        {
            var payload=Payload();string cache;
            using(var lease=payload.Prepare()) { Assert.IsNotNull(lease);cache=lease.Directory; }
            string target=cache+".target";Directory.Move(cache,target);
            using(var process=new Process())
            {
                process.StartInfo=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"cmd.exe"),"/d /c mklink /J \""+cache+"\" \""+target+"\"") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
                Assert.IsTrue(process.Start());Assert.IsTrue(process.WaitForExit(2000));Assert.AreEqual(0,process.ExitCode,process.StandardError.ReadToEnd());
            }
            try { Assert.Throws<InvalidDataException>(delegate { payload.Prepare(); });Assert.IsTrue(File.Exists(Path.Combine(target,"WinMemoryCleaner.Sensors.exe"))); }
            finally{Directory.Delete(cache);}
        }
    }
}
#pragma warning restore 1591
