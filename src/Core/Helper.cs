using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Helper
    /// </summary>
    public static class Helper
    {
        /// <summary>
        /// Deletes a file at the specified path if it exists.
        /// </summary>
        /// <param name="path">The full path to the file to delete.</param>
        /// <param name="throwOnException">If true, throws exceptions; otherwise, returns false on failure.</param>
        /// <returns>True if the file was successfully deleted; false if deletion failed or did not exist.</returns>
        public static bool DeleteFile(string path, bool throwOnException = false)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch
            {
                Logger.Debug("Failed to delete file: " + path);

                if (throwOnException)
                    throw;
            }

            return false;
        }

        /// <summary>
        /// Converts the specified JSON string to an object of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj">The object.</param>
        /// <returns></returns>
        public static T Deserialize<T>(string obj)
        {
            return new JavaScriptSerializer().Deserialize<T>(obj);
        }

        /// <summary>
        /// Formats a minified JSON string into a pretty-printed format with proper indentation and line breaks.
        /// </summary>
        /// <param name="json">The minified JSON string to format.</param>
        /// <returns>A formatted JSON string with indentation and line breaks.</returns>
        public static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            var sb = new StringBuilder(json.Length * 2);
            var indent = "  ";
            var level = 0;
            var inString = false;
            var escapeNext = false;

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];

                if (escapeNext)
                {
                    sb.Append(c);
                    escapeNext = false;
                    continue;
                }

                if (c == '\\')
                {
                    sb.Append(c);
                    escapeNext = true;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append(c);
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '{':
                    case '[':
                        sb.Append(c);
                        sb.Append(Environment.NewLine);
                        level++;

                        for (var j = 0; j < level; j++)
                            sb.Append(indent);
                        break;

                    case '}':
                    case ']':
                        sb.Append(Environment.NewLine);
                        level--;
                        
                        for (var j = 0; j < level; j++)
                            sb.Append(indent);

                        sb.Append(c);
                        break;

                    case ',':
                        sb.Append(c);
                        sb.Append(Environment.NewLine);
                        
                        for (var j = 0; j < level; j++)
                            sb.Append(indent);
                        break;

                    case ':':
                        sb.Append(c);
                        sb.Append(' ');
                        break;

                    case ' ':
                    case '\t':
                    case '\r':
                    case '\n':
                        // Skip whitespace outside strings
                        break;

                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the executable application's path
        /// </summary>
        public static string GetExecutablePath()
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule.FileName;

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch 
            {
                // ignored
            }

            try
            {
                var entry = Assembly.GetEntryAssembly();

                if (entry != null && !string.IsNullOrEmpty(entry.Location))
                    return entry.Location;
            }
            catch
            {
                // ignored
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
        }

        /// <summary>
        /// Computes the SHA-256 checksum of a file.
        /// </summary>
        /// <param name="path">The full path of the file to hash.</param>
        /// <returns>The checksum as lowercase hexadecimal text, or null when the file is missing or cannot be read.</returns>
        public static string GetFileSha256(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                using (var sha256 = SHA256.Create())
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var hash = sha256.ComputeHash(stream);
                    var text = new StringBuilder(hash.Length * 2);

                    foreach (var value in hash)
                    {
                        text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                    }

                    return text.ToString();
                }
            }
            catch (Exception e)
            {
                Logger.Debug("Failed to compute the SHA-256 checksum of " + path + ": " + e.GetMessage());

                return null;
            }
        }

        /// <summary>
        /// Extracts the first SHA-256 checksum found in a published checksum text.
        /// Accepts a bare hash, the "HASH  file" lines written by sha256sum and the
        /// "SHA256 (file) = HASH" lines written by certutil.
        /// </summary>
        /// <param name="text">The published checksum text.</param>
        /// <returns>The checksum as published, or null when the text contains no SHA-256 checksum.</returns>
        public static string GetSha256FromChecksumText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var match = Regex.Match(text, @"(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])");

            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// Gets the application's assembly version
        /// </summary>
        public static Version GetVersion()
        {
            try
            {
                return (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version ?? new Version(0, 0, 0, 0);
            }
            catch
            {
                return new Version(0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Determines whether automatic updates can be used on this machine. Requires a configured
        /// release channel with HTTPS version, executable and checksum sources, and a supported
        /// Windows version (GitHub requires TLS/SNI; Windows XP/2003 and earlier cannot connect).
        /// </summary>
        /// <returns>True if updates are supported; otherwise, false.</returns>
        public static bool IsAutoUpdateSupported
        {
            get
            {
                if (!Constants.App.ReleaseChannelConfigured || App.IsTestBuild ||
                    Constants.App.Repository.AssemblyInfoUri == null || Constants.App.Repository.LatestExeUri == null)
                    return false;

                if (Constants.App.Repository.AssemblyInfoUri.Scheme != Uri.UriSchemeHttps ||
                    Constants.App.Repository.LatestExeUri.Scheme != Uri.UriSchemeHttps)
                    return false;

                // A downloaded update is only installed after its published checksum matched, so a
                // channel without a verifiable checksum source does not support updates.
                if (!Updater.IsVerifiableUpdateChannel(Constants.App.Repository.LatestExeHashUri))
                    return false;

                try
                {
                    var os = Environment.OSVersion;

                    if (os.Version != null && os.Version.Major < 6)
                        return false; // Windows XP/2003 and earlier
                }
                catch
                {
                }

                return true;
            }
        }

        /// <summary>
        /// Gets the string name of a property or field.
        /// </summary>
        /// <typeparam name="T">The type of the member.</typeparam>
        /// <param name="expression">A lambda expression that accesses the member.</param>
        /// <returns>The string name of the member.</returns>
        public static string NameOf<T>(Expression<Func<T>> expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            var memberExpression = expression.Body as MemberExpression;

            if (memberExpression == null)
                throw new ArgumentException("Expression must be a simple member access (e.g., () => myObject.MyProperty).");

            return memberExpression.Member.Name;
        }

        /// <summary>
        /// Reads the embedded resource.
        /// </summary>
        /// <param name="name">Name of the resource.</param>
        /// <returns></returns>
        public static T ReadEmbeddedResource<T>(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream))
            {
                return Deserialize<T>(reader.ReadToEnd());
            }
        }

        /// <summary>
        /// Converts the specified object to a JSON string
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="minified">If true, produces compact JSON without formatting; otherwise, formats with indentation for readability. Default is false.</param>
        /// <returns>A JSON string representation of the object.</returns>
        public static string Serialize(IJsonSerializable obj, bool minified = false)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            var json = new JavaScriptSerializer().Serialize(obj.ToJson());

            return minified ? json : FormatJson(json);
        }

        /// <summary>
        /// Creates or deletes the Start Menu shortcut based on the specified flag.
        /// </summary>
        /// <param name="create">If true, creates the shortcut; if false, deletes it.</param>
        public static void StartMenuShortcut(bool create)
        {
            if (App.IsTestBuild)
                return;

            try
            {
                var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Constants.App.Shortcut);

                if (create)
                {
                    var link = (ShellInterop.IShellLink)new ShellInterop.ShellLink();

                    link.SetDescription(Constants.App.Title);
                    link.SetPath(App.Path);
                    link.SetWorkingDirectory(Path.GetDirectoryName(App.Path));

                    IPersistFile file = (IPersistFile)link;

                    file.Save(shortcutPath, false);
                }
                else
                    DeleteFile(shortcutPath);
            }
            catch (Exception e)
            {
                Logger.Debug("Failed to " + (create ? "create" : "delete") + " Start Menu shortcut: " + e.GetMessage());
            }
        }

        /// <summary>
        /// Converts to hexcode.
        /// </summary>
        /// <param name="red">The red.</param>
        /// <param name="green">The green.</param>
        /// <param name="blue">The blue.</param>
        /// <param name="alpha">The alpha.</param>
        /// <returns></returns>
        public static string ToHexCode(byte red, byte green, byte blue, byte? alpha = null)
        {
            if (alpha != null)
                return string.Format(Localizer.Culture, "#{0:X2}{1:X2}{2:X2}{3:X2}", alpha, red, green, blue);

            return string.Format(Localizer.Culture, "#{0:X2}{1:X2}{2:X2}", red, green, blue);
        }
    }
}