using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    /// <summary>Prepares only the versioned, embedded sensor payload; never executes an installer.</summary>
    internal sealed class SensorPayload
    {
        private const string ResourcePrefix = "WinMemoryCleaner.Sensors.Payload.";
        private readonly string _root;
        private readonly string _directory;
        private readonly string _manifestHash;
        private readonly IList<PayloadFile> _files;
        private readonly Func<string, Stream> _resource;

        internal SensorPayload(string root, byte[] manifest, Func<string, Stream> resource)
        {
            if (String.IsNullOrEmpty(root) || manifest == null || resource == null) throw new ArgumentException("Missing sensor payload input.");
            _root = Path.GetFullPath(root);
            if (_root.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidDataException("Sensor cache must be on a local path.");
            _files = ParseManifest(manifest);
            using (var input = new MemoryStream(manifest, false)) _manifestHash = Hash(input);
            _directory = Path.Combine(_root, _manifestHash);
            _resource = resource;
        }

        internal static SensorPayload CreateDefault()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream input = assembly.GetManifestResourceStream(ResourcePrefix + "manifest.json"))
            using (var output = new MemoryStream())
            {
                if (input == null) throw new InvalidDataException("Embedded sensor manifest is missing.");
                var buffer = new byte[4096]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                { if (output.Length + count > 65536) throw new InvalidDataException("Sensor manifest is too large."); output.Write(buffer, 0, count); }
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"WinMemoryCleaner.Community\Sensors");
                return new SensorPayload(root, output.ToArray(), assembly.GetManifestResourceStream);
            }
        }

        internal SensorPayloadLease Prepare()
        {
            using (var mutex = new Mutex(false, @"Local\WinMemoryCleaner.SensorPayload." + _manifestHash))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(5000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Sensor payload preparation is busy.");
                try
                {
                    CheckDirectoryChain(_root); System.IO.Directory.CreateDirectory(_root);
                    using (var rootLease = new SensorPayloadLease(_root))
                    {
                        rootLease.LockDirectories();
                        string stagingPrefix = ".prepare-" + _manifestHash.Substring(0, 20) + "-";
                        foreach (string abandoned in System.IO.Directory.GetDirectories(_root, stagingPrefix + "*")) DeleteStaging(abandoned);
                        SensorPayloadLease existing = OpenVerified();
                        if (existing != null) return existing;
                        if (System.IO.Directory.Exists(_directory)) { CheckTree(_directory); System.IO.Directory.Delete(_directory, true); }
                        string staging = Path.Combine(_root, stagingPrefix + Guid.NewGuid().ToString("N"));
                        if (System.IO.Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Sensor staging path already exists.");
                        System.IO.Directory.CreateDirectory(staging);
                        try
                        {
                            foreach (PayloadFile file in _files) Extract(staging, file);
                            PublishStaging(staging);
                            SensorPayloadLease ready = OpenVerified();
                            if (ready == null) throw new InvalidDataException("Prepared sensor payload failed verification.");
                            return ready;
                        }
                        finally { DeleteStaging(staging); }
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static void DeleteStaging(string staging)
        {
            try
            {
                if (!System.IO.Directory.Exists(staging)) return;
                CheckTree(staging);
                System.IO.Directory.Delete(staging, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException) { }
        }

        private void PublishStaging(string staging)
        {
            // A newly extracted EXE can briefly be held by a scanner without delete sharing.
            // Retry only transient move failures on the preparation worker, never accept stale bytes.
            for (int attempt = 0; ; attempt++)
            {
                try { System.IO.Directory.Move(staging, _directory); return; }
                catch (IOException error)
                {
                    int code = Marshal.GetHRForException(error) & 0xFFFF;
                    if (attempt >= 10 || (code != 5 && code != 32 && code != 33)) throw;
                    CheckTree(staging);
                    if (System.IO.Directory.Exists(_directory) || File.Exists(_directory)) throw;
                    Thread.Sleep(100);
                }
            }
        }

        private SensorPayloadLease OpenVerified()
        {
            if (!System.IO.Directory.Exists(_directory)) return null;
            SensorPayloadLease lease = null;
            try
            {
                lease = new SensorPayloadLease(_directory);
                lease.LockDirectories();
                string[] entries = System.IO.Directory.GetFileSystemEntries(_directory);
                foreach (string entry in entries)
                    if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Sensor payload contains a reparse point.");
                if (entries.Length != _files.Count) return null;
                var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PayloadFile file in _files) expected.Add(file.Path);
                foreach (string entry in entries)
                    if (!expected.Contains(Path.GetFileName(entry)) || System.IO.Directory.Exists(entry)) return null;
                foreach (PayloadFile file in _files)
                {
                    FileStream input = lease.LockFile(Path.Combine(_directory, file.Path));
                    if (input.Length != file.Length || !String.Equals(Hash(input), file.Hash, StringComparison.Ordinal)) return null;
                }
                SensorPayloadLease verified = lease;
                lease = null; // Transfer every locked handle to the caller.
                return verified;
            }
            finally { if (lease != null) lease.Dispose(); }
        }

        private void Extract(string staging, PayloadFile file)
        {
            using (Stream resource = _resource(file.Resource))
            {
                if (resource == null) throw new InvalidDataException("Embedded sensor file is missing: " + file.Path);
                using (var gzip = new GZipStream(resource, CompressionMode.Decompress))
                using (var output = new FileStream(Path.Combine(staging, file.Path), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    var buffer = new byte[8192]; int count;
                    while ((count = gzip.Read(buffer, 0, buffer.Length)) != 0)
                    { if (output.Length + count > file.Length) throw new InvalidDataException("Sensor file exceeds manifest length."); output.Write(buffer, 0, count); }
                    output.Position = 0;
                    if (output.Length != file.Length || !String.Equals(Hash(output), file.Hash, StringComparison.Ordinal)) throw new InvalidDataException("Embedded sensor file hash/length mismatch: " + file.Path);
                }
            }
        }

        private static IList<PayloadFile> ParseManifest(byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > 65536) throw new InvalidDataException("Invalid sensor manifest length.");
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 8 };
                var manifest = serializer.DeserializeObject(new UTF8Encoding(false, true).GetString(bytes)) as IDictionary<string, object>;
                if (manifest == null || manifest.Count != 3 || !manifest.ContainsKey("version") || !manifest.ContainsKey("protocol") || !manifest.ContainsKey("files") || !(manifest["protocol"] is int) || (int)manifest["protocol"] != 1 || !String.Equals(manifest["version"] as string, SensorProtocol.PayloadVersion, StringComparison.Ordinal)) throw new InvalidDataException("Incompatible sensor manifest.");
                var items = manifest["files"] as IList;
                if (items == null || items.Count < 4 || items.Count > 64) throw new InvalidDataException("Invalid sensor file count.");
                var files = new List<PayloadFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
                foreach (object item in items)
                {
                    var row = item as IDictionary<string, object>;
                    if (row == null || row.Count != 4 || !row.ContainsKey("path") || !row.ContainsKey("length") || !row.ContainsKey("sha256") || !row.ContainsKey("resource")) throw new InvalidDataException("Invalid sensor manifest entry.");
                    string path = row["path"] as string, hash = row["sha256"] as string, resource = row["resource"] as string;
                    if (String.IsNullOrEmpty(path) || path.Length > 128 || !Regex.IsMatch(path, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant) || path.EndsWith(".", StringComparison.Ordinal) || Regex.IsMatch(path, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || !names.Add(path)) throw new InvalidDataException("Unsafe or duplicate sensor filename.");
                    if (!(row["length"] is int) && !(row["length"] is long)) throw new InvalidDataException("Invalid sensor file length.");
                    long length = Convert.ToInt64(row["length"], CultureInfo.InvariantCulture);
                    total += length;
                    if (length <= 0 || length > 67108864 || total > 134217728 || hash == null || !Regex.IsMatch(hash, @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant) || resource != ResourcePrefix + path + ".gz") throw new InvalidDataException("Invalid sensor file metadata.");
                    files.Add(new PayloadFile(path, length, hash, resource));
                }
                foreach (string required in new[] { "WinMemoryCleaner.Sensors.exe", "WinMemoryCleaner.Sensors.exe.config", "LibreHardwareMonitorLib.dll", "PawnIO_setup.exe" })
                    if (!names.Contains(required)) throw new InvalidDataException("Required sensor file is absent.");
                return files;
            }
            catch (ArgumentException error) { throw new InvalidDataException("Malformed sensor manifest.", error); }
        }

        internal static string Hash(Stream input)
        {
            using (var hash = SHA256.Create())
            { var text = new StringBuilder(); foreach (byte value in hash.ComputeHash(input)) text.Append(value.ToString("x2", CultureInfo.InvariantCulture)); return text.ToString(); }
        }

        private static void CheckDirectoryChain(string path)
        {
            foreach (string part in PayloadFileAccess.DirectoryChain(path))
                if (System.IO.Directory.Exists(part) || File.Exists(part))
                {
                    FileAttributes attributes = File.GetAttributes(part);
                    if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) throw new InvalidDataException("Unsafe sensor cache directory.");
                }
        }

        private static void CheckTree(string path)
        {
            CheckDirectoryChain(path);
            foreach (string entry in System.IO.Directory.GetFileSystemEntries(path))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Sensor cache contains a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) CheckTree(entry);
            }
        }

        private sealed class PayloadFile
        {
            internal readonly string Path, Hash, Resource;
            internal readonly long Length;
            internal PayloadFile(string path, long length, string hash, string resource) { Path = path; Length = length; Hash = hash; Resource = resource; }
        }
    }

    /// <summary>Owns non-delete-sharing directory handles and read-only file handles until backend/installer exit.</summary>
    internal sealed class SensorPayloadLease : IDisposable
    {
        private readonly List<IDisposable> _locks = new List<IDisposable>();
        private readonly string _directory;
        private bool _disposed;
        internal SensorPayloadLease(string directory) { _directory = directory; }
        internal string Directory { get { return _directory; } }
        internal string Executable { get { return Path.Combine(_directory, "WinMemoryCleaner.Sensors.exe"); } }
        internal string Installer { get { return Path.Combine(_directory, "PawnIO_setup.exe"); } }
        internal void LockDirectories() { foreach (string part in PayloadFileAccess.DirectoryChain(_directory)) _locks.Add(PayloadFileAccess.Open(part, true)); }
        internal FileStream LockFile(string path)
        {
            SafeFileHandle handle = null;
            FileStream input = null;
            try
            {
                handle = PayloadFileAccess.Open(path, false);
                _locks.Add(handle);
                SafeFileHandle lockedHandle = handle;
                handle = null; // The lease explicitly retains the shared SafeFileHandle.
                input = new FileStream(lockedHandle, FileAccess.Read, 8192, false);
                _locks.Add(input);
                FileStream locked = input;
                input = null; // The lease retains the stream until backend/installer exit.
                return locked;
            }
            finally
            {
                if (input != null) input.Dispose();
                if (handle != null) handle.Dispose();
            }
        }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        private void Dispose(bool disposing)
        { if (!_disposed && disposing) { for (int i = _locks.Count - 1; i >= 0; i--) _locks[i].Dispose(); _locks.Clear(); _disposed = true; } }
    }

    internal static class PayloadFileAccess
    {
        internal static IList<string> DirectoryChain(string path)
        { var parts = new List<string>(); for (string part = Path.GetFullPath(path); !String.IsNullOrEmpty(part); part = Path.GetDirectoryName(part)) parts.Add(part); parts.Reverse(); return parts; }
        internal static SafeFileHandle Open(string path, bool directory)
        {
            SafeFileHandle handle = CreateFile(path, directory ? 0x80U : 0x80000000U, directory ? 3U : 1U, IntPtr.Zero, 3, 0x00200000U | (directory ? 0x02000000U : 0U), IntPtr.Zero);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("Cannot lock sensor payload path (" + error.ToString(CultureInfo.InvariantCulture) + ")."); }
            try
            {
                FileInformation info;
                if (!GetFileInformationByHandle(handle, out info)) throw new IOException("Cannot inspect sensor payload handle.");
                if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory) throw new InvalidDataException("Sensor payload path is a reparse point or wrong file type.");
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
    }
}
