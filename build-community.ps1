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
    [string]$MSBuildPath = '',
    [string]$SensorCompilerPath = ''
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
if ($Package -and -not $canonicalToolchain) { throw 'Packaging requires Visual Studio MSBuild and the complete Roslyn analyzer set.' }
if ($canonicalToolchain) {
    $msbuildFile = Get-Item -LiteralPath $msbuild
    $msbuildInfo = $msbuildFile.VersionInfo
    if ([int]$msbuildInfo.FileMajorPart -lt 16) { throw 'Canonical builds require Visual Studio MSBuild 16 or newer; the in-box compiler is development-only.' }
    # MSBuild's ProductName is not a stable Visual Studio identifier (including VS 18).
    # Resolve the executable against the official installer inventory instead.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Cannot verify a Visual Studio installation without its official vswhere inventory.' }
    $installedBuilds = @(& $vswhere -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\MSBuild.exe')
    if ($LASTEXITCODE -ne 0 -or @($installedBuilds | Where-Object { [String]::Equals($_, $msbuildFile.FullName, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) { throw 'The selected MSBuild is not in a registered Visual Studio installation.' }
    $SensorCompilerPath = Join-Path (Split-Path -Parent $msbuild) 'Roslyn\csc.exe'
    if (-not (Test-Path -LiteralPath $SensorCompilerPath)) { throw 'The canonical MSBuild installation is missing its Roslyn C# compiler.' }
}
if (-not (Test-Path $msbuild)) { throw "MSBuild not found: $msbuild" }

Write-Host ("TOOLCHAIN: {0} {1} ({2})" -f $msbuild, (Get-Item $msbuild).VersionInfo.FileVersion, $(if ($canonicalToolchain) { 'released toolchain' } else { 'development only' }))
if (-not $canonicalToolchain) {
    Write-Host 'NOTE: the in-box tools version 4.0 does not load the analyzers of AllRules.ruleset. Pass -MSBuildPath to build the way CI does.'
}

# The isolated sensor backend and its verified payload are required build inputs: the payload is
# embedded into the executable, so it must be produced from the same sources as this build.
if (-not (Test-Path (Join-Path $repo 'build\sensors\lib\LibreHardwareMonitorLib.dll'))) {
    Write-Host 'SENSORS: restoring the pinned sensor dependencies'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\restore-sensors.ps1') -ProjectRoot $repo
    if ($LASTEXITCODE -ne 0) { throw 'Sensor dependency restore failed.' }
}
$sensorArguments = @()
if (-not [string]::IsNullOrWhiteSpace($SensorCompilerPath)) { $sensorArguments = @('-CompilerPath', $SensorCompilerPath) }
if ($Tests) { $sensorArguments += '-Tests' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\build-sensors.ps1') @sensorArguments
if ($LASTEXITCODE -ne 0) { throw 'Sensor payload build failed.' }
Write-Host 'SENSORS: payload staged in build\sensors\payload'

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

$results = Join-Path $repo 'build\test-results'
if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }
New-Item -ItemType Directory -Path $results -Force | Out-Null
function Verify-EmbeddedPayload([string]$path,[string]$name) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\verify-sensor-payload.ps1') -Executable $path -ReportPath (Join-Path $results $name)
    if ($LASTEXITCODE -ne 0) { throw "Same-build embedded sensor payload verification failed: $path" }
}
function Assert-TestReport([string]$path,[string]$minimum) {
    if (-not (Test-Path -LiteralPath $path)) { throw "NUnit did not produce a report: $path" }
    $report = New-Object System.Xml.XmlDocument
    $report.Load($path)
    $root = $report.DocumentElement
    if ([int]$root.total -lt [int]$minimum -or [int]$root.errors -ne 0 -or [int]$root.failures -ne 0 -or [int]$root.'not-run' -ne 0) { throw "NUnit report did not pass its safety gate: $path" }
}
function Prepare-TestRunner() {
    $source = Join-Path $src 'packages\NUnit.Runners.2.6.4\tools'
    if (-not (Test-Path -LiteralPath (Join-Path $source 'nunit-console.exe'))) { throw 'Missing restored NUnit 2.6.4 runner.' }
    $directory = Join-Path $repo 'build\nunit'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $directory -Recurse -Force
    foreach ($name in @('nunit-console.exe','nunit-agent.exe','nunit-agent-x86.exe')) {
        $configPath = Join-Path $directory ($name + '.config')
        $config = New-Object System.Xml.XmlDocument
        $config.Load($configPath)
        $startup = $config.SelectSingleNode('/configuration/startup')
        if ($null -eq $startup) { $startup = $config.CreateElement('startup'); [void]$config.DocumentElement.AppendChild($startup) }
        $startup.SetAttribute('useLegacyV2RuntimeActivationPolicy','true')
        foreach ($old in @($startup.SelectNodes('supportedRuntime'))) { [void]$startup.RemoveChild($old) }
        $runtime = $config.CreateElement('supportedRuntime')
        $runtime.SetAttribute('version','v4.0'); [void]$startup.AppendChild($runtime)
        $config.Save($configPath)
    }
    return (Join-Path $directory 'nunit-console.exe')
}


$exe = Join-Path $src 'bin\Release\WinMemoryCleaner.Community.exe'
Assert-NotLocked $exe
Invoke-Build 'false'
if (-not (Test-Path $exe)) { throw "The production build did not produce $exe" }
Write-Host "PRODUCTION OK: $exe"
Write-Host ("SHA256: {0}" -f (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant())

Verify-EmbeddedPayload $exe 'production-payload.json'

if ($Tests) {
    $testExe = Join-Path $src 'bin\Test\WinMemoryCleaner.Community.exe'
    Assert-NotLocked $testExe
    Invoke-Build 'true'
    $runner = Prepare-TestRunner

    Verify-EmbeddedPayload $testExe 'test-payload.json'
    $xml = Join-Path $results 'TestResults.xml'
    $sensorXml = Join-Path $results 'SensorTestResults.xml'
    $sensorTests = Join-Path $repo 'build\sensors\tests\WinMemoryCleaner.Sensors.Tests.dll'
    if (-not (Test-Path -LiteralPath $sensorTests)) { throw 'The same-build backend safety assembly is missing.' }

    & $runner $sensorTests /framework:net-4.0 /exclude:Manual,Desktop /labels /timeout:60000 /noshadow /nologo "/work:$results" "/xml:$sensorXml"
    if ($LASTEXITCODE -ne 0) { throw "Backend safe test suite failed (results: $sensorXml)" }
    Assert-TestReport $sensorXml 36

    # /labels prints every test name as it starts, so a CI log always shows which test
    # is running when something stalls. /timeout bounds every single test: a hung test
    # is aborted after 60 seconds and reported as a failure instead of holding the
    # headless runner until the job timeout.
    & $runner $testExe /framework:net-4.0 /exclude:Manual,Desktop /labels /timeout:60000 /noshadow /nologo "/work:$results" "/xml:$xml"
    if ($LASTEXITCODE -ne 0) { throw "Safe test suite failed (results: $xml)" }

    Assert-TestReport $xml 554
    Write-Host "TESTS OK (excluded categories: Manual, Desktop): $xml"

    $sourceBundle = Join-Path $repo ("build\sensors\WinMemoryCleaner.Community-$version-sensor-sources.zip")
    if (Test-Path -LiteralPath $sourceBundle) { Remove-Item -LiteralPath $sourceBundle -Force }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\package-sensor-sources.ps1') -ProjectRoot $repo -OutputPath $sourceBundle -ReportPath (Join-Path $results 'source-bundle.json')
    if ($LASTEXITCODE -ne 0) { throw 'Locked corresponding-source bundle generation failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\test-sensor-sources.ps1') -ProjectRoot $repo -ReportPath (Join-Path $results 'SourceBundleTests.json')
    if ($LASTEXITCODE -ne 0) { throw 'Corresponding-source integrity safety tests failed.' }
}

if ($Package) {
    if (-not $canonicalToolchain) { throw 'Packaging requires Visual Studio MSBuild and the complete Roslyn analyzer set.' }
    if (-not $Tests) { throw 'Packaging requires -Tests: both safety suites, embedded verification and corresponding-source checks must pass in this build.' }

    $dist = Join-Path $repo ("dist\WinMemoryCleaner.Community-" + $version)
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Path $dist -Force | Out-Null

    $exeName = Split-Path -Leaf $exe
    Copy-Item $exe $dist
    foreach ($doc in @('README.md', 'LICENSE', 'CREDITS.md', 'UPSTREAM-TRACKING.md')) {
        Copy-Item (Join-Path $repo $doc) $dist
    }

    Copy-Item (Join-Path $repo 'sensors\THIRD-PARTY-NOTICES.md') (Join-Path $dist 'SENSOR-THIRD-PARTY-NOTICES.md')
    Copy-Item (Join-Path $repo 'sensors\SOURCE-README.md') (Join-Path $dist 'SENSOR-SOURCES.md')
    $licenseDirectory = Join-Path $dist 'SENSOR-LICENSES'
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    $dependencyLock = [IO.File]::ReadAllText((Join-Path $repo 'sensors\dependencies.lock.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($license in $dependencyLock.licenses) { Copy-Item -LiteralPath (Join-Path $repo ('build\sensors\inputs\' + $license.name)) -Destination $licenseDirectory }
    Copy-Item (Join-Path $repo 'docs\monitoring-guide.zh-CN.md') $dist
    Copy-Item (Join-Path $repo 'docs\universal-temperature-report.zh-CN.md') $dist
    Verify-EmbeddedPayload (Join-Path $dist $exeName) 'packaged-payload.json'
    $sourceZip = Join-Path (Split-Path -Parent $dist) (Split-Path -Leaf $sourceBundle)
    Copy-Item -LiteralPath $sourceBundle -Destination $sourceZip -Force
    "$((Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($sourceZip))" | Out-File -LiteralPath ($sourceZip + '.sha256') -Encoding ascii

    # An update is only installed after its checksum matched, and the announced version is read
    # from AssemblyInfo.txt. Both are release assets of the update channel, so they are generated
    # here from the shipped bytes and never written by hand.
    $packagedExe = Join-Path $dist $exeName
    $packagedHash = (Get-FileHash $packagedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    "$packagedHash  $exeName" | Out-File (Join-Path $dist ($exeName + '.sha256')) -Encoding ascii
    "[assembly: AssemblyVersion(""$version"")]" | Out-File (Join-Path $dist 'AssemblyInfo.txt') -Encoding ascii

    $manifest = Join-Path $dist 'checksums.txt'
    @(Get-ChildItem $dist -File -Recurse | Where-Object { $_.FullName -ne $manifest } | Sort-Object FullName) | ForEach-Object {
        $relativeName = $_.FullName.Substring($dist.Length + 1).Replace('\', '/')
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relativeName" | Out-File $manifest -Append -Encoding ascii
    }

    $zip = Join-Path $repo ("dist\WinMemoryCleaner.Community-$version.zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip

    Write-Host "PACKAGE OK: $dist"
    Write-Host "CORRESPONDING SOURCE OK: $sourceZip"
    Write-Host "ARCHIVE OK: $zip"
    Write-Host ("ARCHIVE SHA256: {0}" -f (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())
}
