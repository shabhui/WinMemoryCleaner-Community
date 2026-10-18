# Third-party sensor components

WinMemoryCleaner Community's optional isolated sensor backend uses the following unmodified, pinned binary components. `dependencies.lock.json` records package hashes and selected Windows x64 runtime file hashes. Own backend source retains this repository's license and C#4/analyzer requirements; third-party binary source is not represented as satisfying our legacy compiler rules.

| Component | Version | License | Corresponding source |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | [3d331e3370efb858411f19511373eff65a218701](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701) |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | [25319eae5781e75bcf141e844ceab2afe94d40ea](https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea) |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | [c70b735c6cec123ee8a046ac4a0bc6c606f52cf0](https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0) |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | [3b47b960e0830fef344624ad5e389675d5f0a1ce](https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce) |
| HidSharp | 2.6.4 | Unlicense/permissive bundled third-party notices | [Seekye](https://software.seekye.com/hidsharp); exact `LICENSE.txt` is included as `HidSharp-LICENSE.txt` |
| System.Memory / Buffers / Numerics.Vectors / Runtime.CompilerServices.Unsafe | 4.6.3 / 4.6.1 / 4.6.1 / 6.1.2 | MIT | [dotnet maintenance-packages](https://github.com/dotnet/maintenance-packages); NuGet packages retain their exact repository commits |
| System.Threading.AccessControl / Security.AccessControl / Security.Principal.Windows | 10.0.3 / 6.0.0 / 5.0.0 | MIT | [dotnet runtime](https://github.com/dotnet/runtime), [dotnet](https://github.com/dotnet/dotnet); exact package versions/hashes in lock |
| Official PawnIO standard driver | 2.1.0.0 | GPL-2.0-or-later with upstream IOCTL linking exception | [driver/API/tool tag 2.1.0](https://github.com/namazso/PawnIO/tree/e1043d7750f501b6b0d3ac0a71bd5b9406cd17b6); userspace library/tool are LGPL-2.1-or-later |
| PawnIO modules embedded by LHM | 0.2.2 | LGPL-2.1 | [e12a858d952461ee2e919897cacbff7f905fe370](https://github.com/namazso/PawnIO.Modules/tree/e12a858d952461ee2e919897cacbff7f905fe370); all 12 binaries match this release, despite the older 0.1.6 note in upstream LHM |
| PawnPP interpreter within the driver | pinned submodule | MPL-2.0 | [e64e4c37b2d8ba0d8ee57205faf8183aee12c438](https://github.com/namazso/PawnPP/tree/e64e4c37b2d8ba0d8ee57205faf8183aee12c438) |
| Official signed installer wrapper | 2.1.0.0 | Unmodified vendor distribution | [official release](https://github.com/namazso/PawnIO.Setup/releases/tag/2.1.0); wrapper source is not publicly available and is not included in the corresponding-source bundle |

Full MPL-2.0, .NET MIT, HidSharp notices, PawnIO GPLv2 and LGPLv2.1 texts, and the complete PawnIO linking exception are embedded with the payload. The exception permits independently licensed programs communicating solely through IOCTL; **it does not exempt modules loaded over the Pawn interface**. The release's downloadable corresponding-source bundle includes the driver/API/tool, the separately pinned PawnPP submodule and the 0.2.2 module sources/compiler inputs. Source correspondence does not imply a bit-identical signed rebuild; official signing credentials and the installer wrapper source are not available.

System.Management and System.CodeDom NuGet inputs are restored and pinned to resolve the package graph, but their package DLLs are not shipped: the .NET Framework System.Management implementation and its framework dependencies are used. Storage/SPD libraries are dependencies, not enabled hardware categories. No existing sensor settings or saved software-control modes are loaded.

The signed installer is data until the user explicitly confirms the installation in the application. No unrestricted build, silent install, automatic reboot, fan/power/voltage control, scheduled sampling task or sampling service is included. Installing the official shared kernel driver can require elevation/reboot; existing third-party/shared drivers are never overwritten by this application. Hardware acquisition may use read/write-capable native handles/IOCTLs to perform required measurement operations; this is not a claim that every underlying IOCTL has read-only access bits.

This notice does not change any third-party license, guarantee sensor availability on all hardware, or grant permission to disable Windows security protections.
