# WinMemoryCleaner Community - local build & test script
# Usage:
#   .\build-community.ps1              production build only
#   .\build-community.ps1 -Tests       production + isolated test build + safe test suite
#   .\build-community.ps1 -Tests -Package   ... and package dist\ with checksums
param(
    [switch]$Tests,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $repo 'src'
$msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
if (-not (Test-Path $msbuild)) { throw "Framework MSBuild not found: $msbuild" }

function Invoke-Build([string]$testBuild) {
    & $msbuild (Join-Path $src 'WinMemoryCleaner.csproj') /t:Rebuild /tv:4.0 /nr:false /m:1 `
        /p:Configuration=Release /p:Platform=AnyCPU "/p:CommunityTestBuild=$testBuild" /v:minimal /nologo
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
