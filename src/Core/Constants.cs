using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Constants
    /// </summary>
    public static class Constants
    {
        public static class App
        {
            public const int AutoOptimizationMemoryUsageInterval = 5; // Minute
            public const int AutoUpdateInterval = 24; // Hour
            public const string EmbeddedResourcePath = "WinMemoryCleaner.Resources.";
            public const string EmbeddedResourcePathExtension = ".json";
            public const string Id = "58C54AB4-CACF-4F58-BF95-21B021D41C7D";
            public const string License = "GPL-3.0";
            public const string LocalizationResourcePath = EmbeddedResourcePath + "Localization.";
            public const string Name = "WinMemoryCleaner.Community";
            public const string Shortcut = "WinMemoryCleaner Community.lnk";
            public const string ThemesResourcePath = EmbeddedResourcePath + "Themes.";
            public const string Title = "WinMemoryCleaner Community";
            public const string VersionFormat = "{0}.{1}.{2}";
            public const string Attribution = "Based on Windows Memory Cleaner by Igor Mundstein. Unofficial community continuation; not an endorsed successor.";

            public static class Author
            {
                public const string Name = "Igor Mundstein";
            }

            public static class Maintainer
            {
                public const string Name = "shabhui and community contributors";
            }

            // The community release channel publishes the executable, its SHA-256 checksum and the
            // announced version as assets of this repository's releases, so every download can be
            // verified before it is installed. A compile-time switch, hence a constant.
            public const bool ReleaseChannelConfigured = true;

            public static class CommandLineArgument
            {
                public const string Install = "Install";
                public const string Reset = "Reset";
                public const string Service = "Service";
                public const string Uninstall = "Uninstall";
            }

            public static class Defaults
            {
                public static readonly string Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }

            public static class Donation
            {
                public const string Attribution = "These links support the original author, Igor Mundstein, not the Community maintainers.";
                public static readonly Uri BitcoinUri = new Uri("https://www.blockchain.com/explorer/addresses/btc/bc1qu884q5r2uqugvdhyk8l6waakumeve7jykqp7ap");
                public static readonly Uri EthereumUri = new Uri("https://www.blockchain.com/explorer/addresses/eth/0xb71A94733B0578D155D9A765E0d2C4dA0f44156d");
                public static readonly Uri GitHubSponsorUri = new Uri("https://github.com/sponsors/IgorMundstein");
                public static readonly Uri KofiUri = new Uri("https://ko-fi.com/igormundstein");
            }

            public static class Registry
            {
                public static class Key
                {
                    public const string ProcessExclusionList = @"SOFTWARE\WinMemoryCleaner.Community\ProcessExclusionList";
                    public const string Settings = @"SOFTWARE\WinMemoryCleaner.Community";
                }
            }

            public static class Repository
            {
                // The upstream project this continuation is based on; community builds and releases
                // live in this repository.
                public static readonly Uri UpstreamUri = new Uri("https://github.com/IgorMundstein/WinMemoryCleaner");
                public static readonly Uri AboutUri = new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md"));

                // The update channel is served by this repository's releases. The asset names are
                // stable, so releases/latest/download/<name> keeps resolving to the newest release.
                // All three are required: without the announced version there is nothing to compare
                // against, and an update that cannot be verified is never installed.
                public static readonly Uri AssemblyInfoUri = new Uri("https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest/download/AssemblyInfo.txt");
                public static readonly Uri LatestExeUri = new Uri("https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest/download/WinMemoryCleaner.Community.exe");
                public static readonly Uri LatestExeHashUri = new Uri("https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest/download/WinMemoryCleaner.Community.exe.sha256");

                public static readonly Uri DownloadUri = AboutUri;
                public static readonly Uri Uri = AboutUri;
            }
        }

        public static class Windows
        {
            public static class Console
            {
                public const int AttachParentProcess = -1; // ATTACH_PARENT_PROCESS
                public const int StdOutputHandle = -11; // STD_OUTPUT_HANDLE
            }

            public static class DesktopWindowManager
            {
                public static class Attribute
                {
                    public const int BorderColor = 34;
                    public const int WindowCornerPreference = 33;
                }

                public static class Value
                {
                    public const int WindowCornerPreferenceRound = 2;
                }
            }

            public static class Drive
            {
                public const int FsctlDiscardVolumeCache = 589828; // 0x00090054 - FSCTL_DISCARD_VOLUME_CACHE
                public const int IoControlResetWriteOrder = 589832; // 0x000900F8 - FSCTL_RESET_WRITE_ORDER
            }

            public static class File
            {
                public const int FlagsNoBuffering = 536870912; // 0x20000000 - FILE_FLAG_NO_BUFFERING
            }

            public static class Keyboard
            {
                public const int WmHotkey = 786; // 0x312
            }

            public static class Locale
            {
                public static class Name
                {
                    public const string English = "en";
                    public const string PortugueseBrazil = "pt-BR";
                    public const string PortuguesePortugal = "pt-PT";
                    public const string SimplifiedChinese = "zh-Hans";
                    public const string TraditionalChinese = "zh-Hant";
                }
            }

            public static class Privilege
            {
                public const string SeDebugName = "SeDebugPrivilege"; // Required to debug and adjust the memory of a process owned by another account. User Right: Debug programs.
                public const string SeIncreaseQuotaName = "SeIncreaseQuotaPrivilege"; // Required to increase the quota assigned to a process. User Right: Adjust memory quotas for a process.
                public const string SeProfSingleProcessName = "SeProfileSingleProcessPrivilege"; // Required to gather profiling information for a single process. User Right: Profile single process.
            }

            public static class PrivilegeAttribute
            {
                public const int Enabled = 2;
            }

            public static class ShowWindow
            {
                public const int Restore = 9; // SW_RESTORE
            }

            public static class SystemErrorCode
            {
                public const int ErrorAccessDenied = 5; // (ERROR_ACCESS_DENIED) Access is denied
                public const int ErrorInvalidParameter = 87; // (ERROR_INVALID_PARAMETER) The parameter is incorrect
                public const int ErrorNotAllAssigned = 1300; // (ERROR_NOT_ALL_ASSIGNED) Not all privileges or groups referenced are assigned to the caller
                public const int ErrorSuccess = 0; // (ERROR_SUCCESS) The operation completed successfully
            }

            public static class NtStatus
            {
                public const int StatusAccessDenied = unchecked((int)0xC0000022);
                public const int StatusInvalidParameter = unchecked((int)0xC000000D);
            }

            public static class SystemInformationClass
            {
                public const int SystemCombinePhysicalMemoryInformation = 130; // 0x82
                public const int SystemFileCacheInformation = 21; // 0x15
                public const int SystemMemoryListInformation = 80; // 0x50
                public const int SystemRegistryReconciliationInformation = 155; // 0x9B
            }

            public static class SystemMemoryListCommand
            {
                public const int MemoryEmptyWorkingSets = 2;
                public const int MemoryFlushModifiedList = 3;
                public const int MemoryPurgeLowPriorityStandbyList = 5;
                public const int MemoryPurgeStandbyList = 4;
            }
        }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
