# WinMemoryCleaner Community - local build & test script
# Usage:
#   .\build-community.ps1              production build only
#   .\build-community.ps1 -Tests       production + isolated test build + safe test suite
#   .\build-community.ps1 -Tests -Package -MSBuildPath <msbuild.exe>
#                                      ... and package dist\ with the update metadata and checksums
#   .\build-community.ps1 -MSBuildPath <msbuild.exe>
#                                      build with a specific MSBuild. Release artifacts must be
#                                      produced with the Visual Studio MSBuild that CI uses; the
#                                      in-box .NET Framework tools are a development-only compiler.
param(
    [switch]$Tests,
    [switch]$Package,
    [string]$MSBuildPath = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $repo 'src'
$csproj = Join-Path $src 'WinMemoryCleaner.csproj'

# Every toolchain has to compile this source with the same language rules. The in-box
# .NET Framework tools ship a C# 5 compiler, Visual Studio ships Roslyn: the pinned
# LangVersion is what makes a green CI run mean something for the released binary.
$project = [IO.File]::ReadAllText($csproj)
if ($project -notmatch '<LangVersion>\s*4\s*</LangVersion>') {
    throw "src\WinMemoryCleaner.csproj must keep <LangVersion>4</LangVersion> pinned: every toolchain has to apply the same language rules."
}

$assemblyInfo = Join-Path $src 'Properties\AssemblyInfo.cs'
$versionMatch = [regex]::Match([IO.File]::ReadAllText($assemblyInfo), 'AssemblyVersion\("([0-9]+(?:\.[0-9]+){3})"\)')
if (-not $versionMatch.Success) { throw "No AssemblyVersion found in $assemblyInfo" }
$version = $versionMatch.Groups[1].Value

$toolsVersionArg = @()
$canonicalToolchain = -not [string]::IsNullOrWhiteSpace($MSBuildPath)
if (-not $canonicalToolchain) {
    # Default: in-box .NET Framework MSBuild (tools version 4.0). This path needs the system
    # .NET Framework 4.0 targeting pack; the restored Microsoft.NETFramework.ReferenceAssemblies
    # targets, and the analyzers of AllRules.ruleset, are only imported by a newer toolchain.
    $msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
    $toolsVersionArg = @('/tv:4.0')
} else {
    # Visual Studio MSBuild (tools version != 4.0) imports the restored
    # Microsoft.NETFramework.ReferenceAssemblies.net40 targets, so it also builds on machines
    # without a 4.0 targeting pack, and it runs the Roslyn analyzers.
    $msbuild = $MSBuildPath
}
if (-not (Test-Path $msbuild)) { throw "MSBuild not found: $msbuild" }

Write-Host ("TOOLCHAIN: {0} {1} ({2})" -f $msbuild, (Get-Item $msbuild).VersionInfo.FileVersion, $(if ($canonicalToolchain) { 'released toolchain' } else { 'development only' }))
if (-not $canonicalToolchain) {
    Write-Host 'NOTE: the in-box tools version 4.0 does not load the analyzers of AllRules.ruleset. Pass -MSBuildPath to build the way CI does.'
}

function Assert-NotLocked([string]$path) {
    if (-not (Test-Path $path)) { return }

    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $stream.Dispose()
    } catch {
        $name = [IO.Path]::GetFileNameWithoutExtension($path)
        $running = Get-Process -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1
        $hint = if ($running) { " Stop '$name' (process id $($running.Id)) first." } else { ' Close the process that is holding it first.' }
        throw "Cannot rebuild $path because the file is locked.$hint"
    }
}

function Invoke-Build([string]$testBuild) {
    $buildArgs = @(
        $csproj,
        '/t:Rebuild', '/nr:false', '/m:1',
        '/p:Configuration=Release', '/p:Platform=AnyCPU',
        "/p:CommunityTestBuild=$testBuild",
        '/v:minimal', '/nologo'
    ) + $toolsVersionArg
    & $msbuild @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed (CommunityTestBuild=$testBuild)" }
}

$exe = Join-Path $src 'bin\Release\WinMemoryCleaner.Community.exe'
Assert-NotLocked $exe
Invoke-Build 'false'
if (-not (Test-Path $exe)) { throw "The production build did not produce $exe" }
Write-Host "PRODUCTION OK: $exe"
Write-Host ("SHA256: {0}" -f (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant())

if ($Tests) {
    $testExe = Join-Path $src 'bin\Test\WinMemoryCleaner.Community.exe'
    Assert-NotLocked $testExe
    Invoke-Build 'true'
    $runner = Join-Path $src 'packages\NUnit.Runners.2.6.4\tools\nunit-console.exe'
    if (-not (Test-Path $runner)) { throw "NUnit runner missing (nuget restore src\packages.config -> src\packages)" }

    # Test artifacts stay inside the repository (next to the build) instead of $env:TEMP: this
    # keeps the system drive clean and gives CI a stable report path to publish.
    $results = Join-Path $repo 'build\test-results'
    if (Test-Path $results) { Remove-Item $results -Recurse -Force }
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $xml = Join-Path $results 'TestResults.xml'

    & $runner $testExe /framework:net-4.0 /exclude:Manual,Desktop /noshadow /nologo "/work:$results" "/xml:$xml"
    if ($LASTEXITCODE -ne 0) { throw "Safe test suite failed (results: $xml)" }

    Write-Host "TESTS OK (excluded categories: Manual, Desktop): $xml"
}

if ($Package) {
    if (-not $canonicalToolchain) {
        Write-Warning 'Packaging with the in-box .NET Framework tools (C# 5). Release artifacts must come from the same toolchain as CI: re-run with -MSBuildPath <msbuild.exe> from Visual Studio.'
    }

    $dist = Join-Path $repo ("dist\WinMemoryCleaner.Community-" + $version)
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Path $dist -Force | Out-Null

    $exeName = Split-Path -Leaf $exe
    Copy-Item $exe $dist
    foreach ($doc in @('README.md', 'LICENSE', 'CREDITS.md', 'UPSTREAM-TRACKING.md')) {
        Copy-Item (Join-Path $repo $doc) $dist
    }

    # An update is only installed after its checksum matched, and the announced version is read
    # from AssemblyInfo.txt. Both are release assets of the update channel, so they are generated
    # here from the shipped bytes and never written by hand.
    $packagedExe = Join-Path $dist $exeName
    $packagedHash = (Get-FileHash $packagedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    "$packagedHash  $exeName" | Out-File (Join-Path $dist ($exeName + '.sha256')) -Encoding ascii
    "[assembly: AssemblyVersion(""$version"")]" | Out-File (Join-Path $dist 'AssemblyInfo.txt') -Encoding ascii

    $manifest = Join-Path $dist 'checksums.txt'
    @(Get-ChildItem $dist -File | Where-Object { $_.Name -ne 'checksums.txt' }) | ForEach-Object {
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" | Out-File $manifest -Append -Encoding ascii
    }

    $zip = Join-Path $repo ("dist\WinMemoryCleaner.Community-$version.zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip

    Write-Host "PACKAGE OK: $dist"
    Write-Host "ARCHIVE OK: $zip"
    Write-Host ("ARCHIVE SHA256: {0}" -f (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())
}
