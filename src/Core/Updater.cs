using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Updater
    /// </summary>
    public static class Updater
    {
        private static WebClient _client;
        private static DateTimeOffset _lastCheck = DateTimeOffset.MinValue;

        internal static ProcessStartInfo Process;

        /// <summary>
        /// Installs the downloaded file, but only when it matches the announced version.
        /// </summary>
        /// <param name="updateInfo">The update state (temp file, target path, exe name, version, arguments).</param>
        private static void CompleteUpdate(Tuple<string, string, string, Version, string[]> updateInfo)
        {
            var temp = updateInfo.Item1;
            var path = updateInfo.Item2;
            var exe = updateInfo.Item3;
            var newestVersion = updateInfo.Item4;
            var args = updateInfo.Item5;

            if (!File.Exists(temp) || !AssemblyName.GetAssemblyName(temp).Version.Equals(newestVersion))
            {
                Helper.DeleteFile(temp);

                Logger.Error("The downloaded file does not match the announced version. Update aborted.");

                Reset();
                return;
            }

            Process = new ProcessStartInfo
            {
                Arguments = string.Format(CultureInfo.InvariantCulture, @"/c taskkill /f /im ""{0}"" & move /y ""{1}"" ""{2}"" & start """" ""{2}"" /{3} {4}", exe, temp, path, newestVersion, string.Join(" ", args)),
                CreateNoWindow = true,
                FileName = "cmd",
                RedirectStandardError = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            App.Shutdown();
        }

        /// <summary>
        /// Determines whether an update channel can be verified before an executable from it is installed.
        /// </summary>
        /// <param name="checksumUri">The published checksum location of the update channel.</param>
        /// <returns>True when an HTTPS checksum source is configured; otherwise, false.</returns>
        internal static bool IsVerifiableUpdateChannel(Uri checksumUri)
        {
            return checksumUri != null && checksumUri.IsAbsoluteUri && checksumUri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Determines whether a downloaded file matches the checksum its publisher announced. This is the
        /// only state in which an update may be installed: without a parsable published checksum, or on
        /// any difference, the download is not verified.
        /// </summary>
        /// <param name="path">The downloaded file that is about to be installed.</param>
        /// <param name="publishedChecksum">The content of the published checksum file.</param>
        /// <returns>True when the file exists and its SHA-256 equals the published checksum; otherwise, false.</returns>
        internal static bool IsVerifiedUpdate(string path, string publishedChecksum)
        {
            var expected = Helper.GetSha256FromChecksumText(publishedChecksum);
            var actual = File.Exists(path) ? Helper.GetFileSha256(path) : null;

            return expected != null && actual != null && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static void OnFileDownloadCompleted(object sender, AsyncCompletedEventArgs e)
        {
            Tuple<string, string, string, Version, string[]> updateInfo = null;

            try
            {
                if (e.Error != null)
                    throw new Exception("File download failed.", e.Error);

                if (e.Cancelled)
                    return;

                updateInfo = (Tuple<string, string, string, Version, string[]>)e.UserState;

                if (!IsVerifiableUpdateChannel(Constants.App.Repository.LatestExeHashUri))
                {
                    // Never replace the running executable with a download that cannot be verified.
                    Helper.DeleteFile(updateInfo.Item1);

                    Logger.Error("No checksum source is published for the update channel. Update aborted.");

                    Reset();
                    return;
                }

                // Reuse the same client to fetch the published checksum before anything is installed.
                _client.DownloadStringCompleted -= OnVersionCheckCompleted;
                _client.DownloadStringCompleted += OnHashDownloadCompleted;
                _client.DownloadStringAsync(Constants.App.Repository.LatestExeHashUri, updateInfo);
            }
            catch (Exception ex)
            {
                if (updateInfo != null)
                    Helper.DeleteFile(updateInfo.Item1);

                Logger.Error(ex);

                Reset();
            }
        }

        private static void OnHashDownloadCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            Tuple<string, string, string, Version, string[]> updateInfo = null;

            try
            {
                if (e.Error != null)
                    throw new Exception("Checksum download failed.", e.Error);

                if (e.Cancelled)
                    return;

                updateInfo = (Tuple<string, string, string, Version, string[]>)e.UserState;

                if (!IsVerifiedUpdate(updateInfo.Item1, e.Result))
                {
                    Helper.DeleteFile(updateInfo.Item1);

                    Logger.Error("Checksum verification failed for the downloaded update. Update aborted.");

                    Reset();
                    return;
                }

                CompleteUpdate(updateInfo);
            }
            catch (Exception ex)
            {
                if (updateInfo != null)
                    Helper.DeleteFile(updateInfo.Item1);

                Logger.Error(ex);

                Reset();
            }
        }

        private static void OnVersionCheckCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                if (e.Error != null)
                    throw new Exception("Version check failed.", e.Error);

                if (e.Cancelled)
                    return;

                var assemblyInfo = e.Result;
                var assemblyVersionMatch = Regex.Match(assemblyInfo, @"AssemblyVersion\(""(.*)""\)\]");

                if (!assemblyVersionMatch.Success)
                    return;

                var newestVersion = Version.Parse(assemblyVersionMatch.Groups[1].Value);

                if (App.Version >= newestVersion)
                {
                    Reset();
                    return;
                }

                var exe = Path.GetFileName(App.Path);
                var temp = Path.Combine(Path.GetTempPath(), exe);

                Helper.DeleteFile(temp);

                var updateInfo = Tuple.Create(temp, App.Path, exe, newestVersion, (string[])e.UserState);

                _client.DownloadFileAsync(Constants.App.Repository.LatestExeUri, temp, updateInfo);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);

                Reset();
            }
        }

        private static void Reset()
        {
            try
            {
                if (_client != null)
                    _client.Dispose();
            }
            finally
            {
                _client = null;
            }

            try
            {
                if (Process != null)
                    Process = null;
            }
            catch
            {
                // ignored
            }
        }

        internal static bool ShouldCheckForUpdates(bool supported, bool enabled, DateTimeOffset now, DateTimeOffset lastCheck)
        {
            return supported && enabled && now.Subtract(lastCheck).TotalHours >= Constants.App.AutoUpdateInterval;
        }

        /// <summary>
        /// Check for new version and update if available
        /// </summary>
        public static void Update(params string[] args)
        {
            try
            {
                if (!ShouldCheckForUpdates(Helper.IsAutoUpdateSupported, Settings.AutoUpdate, DateTimeOffset.Now, _lastCheck))
                    return;

                _lastCheck = DateTimeOffset.Now;

                Reset();

                _client = new WebClient();
                _client.DownloadFileCompleted += new AsyncCompletedEventHandler(OnFileDownloadCompleted);
                _client.DownloadStringCompleted += new DownloadStringCompletedEventHandler(OnVersionCheckCompleted);

                ServicePointManager.DefaultConnectionLimit = 10;
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2; legacy Framework does not support the TLS 1.3 flag.

                _client.DownloadStringAsync(Constants.App.Repository.AssemblyInfoUri, args);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);

                Reset();
            }
        }
    }
}
