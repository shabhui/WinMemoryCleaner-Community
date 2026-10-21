# Corresponding source for the pinned sensor components

This bundle accompanies WinMemoryCleaner Community's unmodified LibreHardwareMonitorLib 0.9.6 sensor payload (`3d331e3370efb858411f19511373eff65a218701`). The application and its own isolated backend are available in the WinMemoryCleaner Community repository; the modern third-party source is not claimed to compile with the application's C#4 rules.

## Included source archives

`sources.lock.json` records the exact commits, download URLs, lengths and SHA-256 hashes. The three upstream archives are included byte-for-byte, without extracting or executing their contents:

- **PawnIO 2.1.0**, `e1043d7750f501b6b0d3ac0a71bd5b9406cd17b6`: driver, `PawnIOLib`, `PawnIOUtil`, headers, INF template and CMake build scripts. Driver code is GPL-2.0-or-later with the upstream IOCTL linking exception; the user-mode library/tool are LGPL-2.1-or-later.
- **PawnPP**, `e64e4c37b2d8ba0d8ee57205faf8183aee12c438`: MPL-2.0 interpreter submodule. GitHub's PawnIO archive does not contain gitlink contents. Extract this archive separately and place its contents in the PawnIO tree's `PawnPP` directory before building.
- **PawnIO.Modules 0.2.2**, `e12a858d952461ee2e919897cacbff7f905fe370`: LGPL-2.1 module `.p` sources, `include/`, build workflow and `_pawn` compiler/source RPM inputs. All 12 signed module binaries embedded by the pinned LHM package match this release. LHM's older resource README says 0.1.6; that description does not identify these binaries correctly.

The complete GPLv2, LGPLv2.1, MPL-2.0 and upstream IOCTL exception texts accompany these archives. The exception covers independent clients communicating solely through device IOCTLs; **it does not exempt programs/modules loaded through the Pawn interface**. See `THIRD-PARTY-NOTICES.md` for the other pinned sensor dependencies and their corresponding-source locations.

## Build inputs and limits

The PawnIO tree documents CMake 3.25 or newer and an EWDK/MSVC C++20 environment. The official binaries' build paths identify EWDK 10.0.26100.0. The driver build additionally uses `inf2cat`, `makecab` and an externally supplied signing script. Microsoft toolchains and the vendor's private signing credentials are not included.

The module tree's workflow uses Oracle Linux 9 and its vendored `_pawn/pawn-4.1.7152-1.el9.x86_64.rpm`; the corresponding source RPM is included in that same tree. Its recorded compiler command is:

```text
pawncc <module>.p -iinclude -C64 -;+ -(+ -p
```

This package provides corresponding source and the upstream build inputs/scripts, **not a bit-identical signed rebuild**. Signing changes the driver and AMX module bytes. Do not replace a trusted signed production driver with an unsigned rebuild or disable Windows security protections to run one.

The official `PawnIO_setup.exe` 2.1.0 wrapper is redistributed unmodified as vendor data. Its own source is not publicly available in `namazso/PawnIO.Setup` and is **not included or offered** by this bundle. The driver, user-mode library/tool and module sources carried by the installer/payload are covered above; this statement is not a claim that the signed installer wrapper can be rebuilt from public source.

Packaging this bundle does not run an installer, launch a hardware backend or open hardware. Installation of the official shared driver remains a separate action requiring explicit user confirmation; an existing shared/unknown/newer driver must not be overwritten.
