using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WinMemoryCleaner
{
    // Read/query access only. No process enumeration, persistence, debug privilege,
    // termination, injection or executable-path publication.
    internal sealed class FramePresentProcess
    {
        private readonly Func<int, IntPtr> _open;
        private readonly Func<IntPtr, long[]> _times;
        private readonly Func<IntPtr, string> _image;
        private readonly Action<IntPtr> _close;
        private readonly Func<int> _foregroundPid;
        private readonly int _selfId;

        internal FramePresentProcess()
            : this(OpenRead, ReadTimes, ReadImage, CloseRead, ForegroundPid, (int)FramePresentNativeMethods.GetCurrentProcessId()) { }

        internal FramePresentProcess(Func<int, IntPtr> open, Func<IntPtr, long[]> times,
            Func<IntPtr, string> image, Action<IntPtr> close, Func<int> foregroundPid, int selfId)
        {
            if (open == null) throw new ArgumentNullException("open");
            if (times == null) throw new ArgumentNullException("times");
            if (image == null) throw new ArgumentNullException("image");
            if (close == null) throw new ArgumentNullException("close");
            if (foregroundPid == null) throw new ArgumentNullException("foregroundPid");
            if (selfId <= 0) throw new ArgumentOutOfRangeException("selfId");
            _open = open; _times = times; _image = image; _close = close;
            _foregroundPid = foregroundPid; _selfId = selfId;
        }

        internal int SelfId { get { return _selfId; } }

        internal string Verify(ProcessSnapshot target)
        {
            if (target == null || target.Id <= 0 || target.Id == _selfId) return "FpsTargetExited";
            if (target.CreationTime <= 0) return "FpsIdentityChanged";
            IntPtr handle;
            try { handle = _open(target.Id); }
            catch (Win32Exception error)
            {
                // OpenProcess reports ERROR_INVALID_PARAMETER for an absent PID.
                // It is an exited identity, not an unsupported ETW filter/platform.
                if (error.NativeErrorCode == 87) return "FpsTargetExited";
                throw;
            }
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return "FpsTargetExited";
            try
            {
                long[] times = _times(handle);
                if (times == null || times.Length != 2 || times[0] <= 0) return "FpsError";
                if (times[1] != 0) return "FpsTargetExited";
                return times[0] == target.CreationTime ? null : "FpsIdentityChanged";
            }
            finally { _close(handle); }
        }

        internal ProcessSnapshot ResolveForeground()
        {
            int id = _foregroundPid();
            if (id <= 0 || id == _selfId) return null;
            IntPtr handle = _open(id);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;
            try
            {
                long[] times = _times(handle);
                if (times == null || times.Length != 2 || times[0] <= 0 || times[1] != 0) return null;
                string name = FrameRateReading.DisplayName(_image(handle));
                return new ProcessSnapshot { Id = id, CreationTime = times[0], Name = name };
            }
            finally { _close(handle); }
        }

        private static IntPtr OpenRead(int id)
        {
            // PROCESS_QUERY_LIMITED_INFORMATION is available since Vista. It
            // intentionally does not grant VM_READ/WRITE, terminate or synchronize.
            IntPtr handle = FramePresentNativeMethods.OpenProcess(0x1000, false, id);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            return handle;
        }

        private static long[] ReadTimes(IntPtr handle)
        {
            long creation, exit, kernel, user;
            if (!FramePresentNativeMethods.GetProcessTimes(handle, out creation, out exit, out kernel, out user))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new[] { creation, exit };
        }

        private static string ReadImage(IntPtr handle)
        {
            const int maximumCharacters = 32768;
            StringBuilder image = new StringBuilder(maximumCharacters);
            uint length = maximumCharacters;
            if (!FramePresentNativeMethods.QueryFullProcessImageNameW(handle, 0, image, ref length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return image.ToString();
        }

        private static void CloseRead(IntPtr handle) { FramePresentNativeMethods.CloseHandle(handle); }

        private static int ForegroundPid()
        {
            IntPtr window = FramePresentNativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero) return 0;
            uint id;
            if (FramePresentNativeMethods.GetWindowThreadProcessId(window, out id) == 0) return 0;
            return id <= int.MaxValue ? (int)id : 0;
        }
    }
}
