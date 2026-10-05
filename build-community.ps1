# WinMemoryCleaner Community - local build & test script
# Usage:
#   .\build-community.ps1              production build only
#   .\build-community.ps1 -Tests       production + isolated test build + safe test suite
#   .\build-community.ps1 -Tests -Package   ... and package dist\ with checksums
#   .\build-community.ps1 -Tests -MSBuildPath <msbuild.exe>
#                                      build with a specific MSBuild (for example Visual
#                                      Studio MSBuild, for machines without the .NET
#                                      Framework 4.0 targeting pack)
param(
    [switch]$Tests,
    [switch]$Package,
    [string]$MSBuildPath = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $repo 'src'

$toolsVersionArg = @()
if ([string]::IsNullOrWhiteSpace($MSBuildPath)) {
    # Default: in-box .NET Framework MSBuild (tools version 4.0). This path needs the system
    # .NET Framework 4.0 targeting pack; the restored Microsoft.NETFramework.ReferenceAssemblies
    # targets are only imported when the tools version is not 4.0.
    $msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
    $toolsVersionArg = @('/tv:4.0')
} else {
    # Visual Studio MSBuild (tools version != 4.0) imports the restored
    # Microsoft.NETFramework.ReferenceAssemblies.net40 targets, so it also builds on machines
    # without a 4.0 targeting pack.
    $msbuild = $MSBuildPath
}
if (-not (Test-Path $msbuild)) { throw "MSBuild not found: $msbuild" }

function Invoke-Build([string]$testBuild) {
    $buildArgs = @(
        (Join-Path $src 'WinMemoryCleaner.csproj'),
        '/t:Rebuild', '/nr:false', '/m:1',
        '/p:Configuration=Release', '/p:Platform=AnyCPU',
        "/p:CommunityTestBuild=$testBuild",
        '/v:minimal', '/nologo'
    ) + $toolsVersionArg
    & $msbuild @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed (CommunityTestBuild=$testBuild)" }
}

Invoke-Build 'false'
$exe = Join-Path $src 'bin\Release\WinMemoryCleaner.Community.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host "PRODUCTION OK: $exe"
Write-Host "SHA256: $hash"

if ($Tests) {
    Invoke-Build 'true'
    $testExe = Join-Path $src 'bin\Test\WinMemoryCleaner.Community.exe'
    $runner = Join-Path $src 'packages\NUnit.Runners.2.6.4\tools\nunit-console.exe'
    if (-not (Test-Path $runner)) { throw "NUnit runner missing (nuget restore src\packages.config -> src\packages)" }
    $work = Join-Path ($env:TEMP) ("wmc-community-tests-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $xml = Join-Path $work 'TestResults.xml'
    & $runner $testExe /framework:net-4.0 /exclude:Manual,Desktop /noshadow /nologo "/work:$work" "/xml:$xml"
    if ($LASTEXITCODE -ne 0) { throw "Safe test suite failed (results: $xml)" }
    Write-Host "TESTS OK (excluded categories: Manual, Desktop): $xml"
}

if ($Package) {
    $version = '3.0.8.1'
    $dist = Join-Path $repo ("dist\WinMemoryCleaner.Community-" + $version)
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    Copy-Item $exe $dist
    foreach ($doc in @('README.md', 'LICENSE', 'CREDITS.md', 'UPSTREAM-TRACKING.md')) {
        Copy-Item (Join-Path $repo $doc) $dist
    }
    $manifest = Join-Path $dist 'checksums.txt'
    Get-ChildItem $dist -File | ForEach-Object {
        $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        "$h  $($_.Name)" | Out-File $manifest -Append -Encoding ascii
    }
    Write-Host "PACKAGE OK: $dist"
}
