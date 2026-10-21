<#
.SYNOPSIS
Builds the pinned isolated sensor backend and stages the payload that is embedded into the application.

.DESCRIPTION
Compiles sensors/*.cs together with shared/Sensors/*.cs using a Roslyn compiler, this repository's complete
AllRules analyzer ruleset and the pinned .NET Framework 4.7.2 reference assemblies. The staged payload contains
the backend executable and configuration, the twelve pinned runtime libraries, the unmodified official PawnIO
installer, the license texts, the third-party notice with corresponding-source information, this lock file and a
build-info document that records the compiler, ruleset, analyzer, reference and source hashes actually used.

Nothing here is executed: the installer stays data until the user explicitly confirms it in the application, and
no hardware is opened. An in-box C# 5 compiler is rejected on purpose because it cannot run the analyzer ruleset;
a build that cannot use Roslyn fails instead of silently producing a weaker payload.

Every staged file is gzip compressed under WinMemoryCleaner.Sensors.Payload.<name>.gz and listed in
sensors-payload.manifest.json with its length and SHA-256, so the application can verify the embedded bytes
before use. tools/build-sensors.ps1 is the only supported producer of that directory.
#>
[CmdletBinding()]
param(
    [string]$CompilerPath = '',
    [string]$OutputDirectory = '',
    [switch]$Tests
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$LockPath = Join-Path $ProjectRoot 'sensors\dependencies.lock.json'
$PayloadVersion = 'lhm-0.9.6-p1'
if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'sensors\WinMemoryCleaner.Sensors.csproj'))) { throw "Run this script from the repository tools directory; '$ProjectRoot' is not the project root." }
$Lock = Get-Content -Raw -Encoding UTF8 -LiteralPath $LockPath | ConvertFrom-Json
if ($Lock.schema -ne 1 -or $Lock.payload -ne $PayloadVersion) { throw 'Unsupported sensor dependency lock.' }
if ([string]::IsNullOrEmpty($OutputDirectory)) { $OutputDirectory = Join-Path $ProjectRoot 'build\sensors\payload' }
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $ProjectRoot $OutputDirectory }

function Get-Sha256([string]$Path)
{
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try
    {
        $stream = [System.IO.File]::OpenRead($Path)
        try { $bytes = $algorithm.ComputeHash($stream) } finally { $stream.Dispose() }
    }
    finally { $algorithm.Dispose() }
    $text = New-Object System.Text.StringBuilder
    foreach ($value in $bytes) { [void]$text.Append($value.ToString('x2',[System.Globalization.CultureInfo]::InvariantCulture)) }
    return $text.ToString()
}

function Assert-PinnedFile([string]$Path,[long]$Bytes,[string]$Hash,[string]$Label)
{
    if (-not (Test-Path -LiteralPath $Path)) { throw "Missing $Label at '$Path'. Run tools\restore-sensors.ps1 first." }
    if ((Get-Item -LiteralPath $Path).Length -ne $Bytes) { throw "$Label length mismatch at '$Path'." }
    if ((Get-Sha256 $Path) -ne $Hash) { throw "$Label hash mismatch at '$Path'." }
}

function Resolve-Compiler([string]$Requested)
{
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrEmpty($Requested)) { $candidates.Add($Requested) }
    if (-not [string]::IsNullOrEmpty($env:WMC_SENSOR_CSC)) { $candidates.Add($env:WMC_SENSOR_CSC) }
    $toolsets = Get-ChildItem -Path (Join-Path $ProjectRoot 'build\sensors\packages') -Recurse -File -Filter 'csc.exe' -ErrorAction SilentlyContinue
    foreach ($toolset in @($toolsets)) { if ($toolset.FullName -match '\\tasks\\net472\\csc\.exe$') { $candidates.Add($toolset.FullName) } }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere)
    {
        foreach ($installation in @(& $vswhere -latest -prerelease -products * -property installationPath))
        {
            if ([string]::IsNullOrWhiteSpace($installation)) { continue }
            $candidates.Add((Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'))
        }
    }
    foreach ($candidate in $candidates) { if (Test-Path -LiteralPath $candidate) { return (Get-Item -LiteralPath $candidate).FullName } }
    throw 'No Roslyn C# compiler was found. Pass -CompilerPath or set WMC_SENSOR_CSC; the analyzer ruleset is mandatory and the in-box C# 5 compiler is not an accepted substitute.'
}

function Read-Analyzers()
{
    $propsPath = Join-Path $ProjectRoot 'sensors\Analyzers.props'
    $packages = Join-Path $ProjectRoot 'src\packages'
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in (Get-Content -LiteralPath $propsPath -Encoding UTF8))
    {
        if ($line -notmatch '<Analyzer Include="\$\(SensorAnalyzerPackages\)([^"]+)"\s*/>') { continue }
        $paths.Add((Join-Path $packages ($Matches[1].Replace('/','\'))))
    }
    if ($paths.Count -eq 0) { throw 'No analyzer was declared in sensors\Analyzers.props; refusing to build without the canonical analyzer set.' }
    foreach ($path in $paths) { if (-not (Test-Path -LiteralPath $path)) { throw "Missing canonical sensor analyzer '$path'. Run tools\restore-sensors.ps1 first." } }
    return $paths
}

$runtimeLibraries = New-Object System.Collections.Generic.List[string]
foreach ($asset in $Lock.assets)
{
    $libraryPath = Join-Path $ProjectRoot ('build\sensors\lib\' + $asset.name)
    Assert-PinnedFile $libraryPath ([long]$asset.bytes) $asset.sha256 ('Runtime library ' + $asset.name)
    $runtimeLibraries.Add($libraryPath)
}
$inputFiles = New-Object System.Collections.Generic.List[object]
foreach ($license in $Lock.licenses)
{
    $licensePath = Join-Path $ProjectRoot ('build\sensors\inputs\' + $license.name)
    Assert-PinnedFile $licensePath ([long]$license.bytes) $license.sha256 ('License text ' + $license.name)
    $inputFiles.Add([pscustomobject]@{ Source = $licensePath; Name = $license.name })
}
$installerPath = Join-Path $ProjectRoot ('build\sensors\inputs\' + $Lock.installer.name)
Assert-PinnedFile $installerPath ([long]$Lock.installer.bytes) $Lock.installer.sha256 'Official PawnIO installer'
$inputFiles.Add([pscustomobject]@{ Source = $installerPath; Name = $Lock.installer.name })
$noticePath = Join-Path $ProjectRoot 'sensors\THIRD-PARTY-NOTICES.md'
if (-not (Test-Path -LiteralPath $noticePath)) { throw 'Missing sensors\THIRD-PARTY-NOTICES.md.' }
$inputFiles.Add([pscustomobject]@{ Source = $noticePath; Name = 'THIRD-PARTY-NOTICES.md' })
$inputFiles.Add([pscustomobject]@{ Source = $LockPath; Name = 'sensors-dependencies.lock.json' })

$referenceRoot = Join-Path $ProjectRoot 'build\sensors\packages\microsoft.netframework.referenceassemblies.net472.1.0.3\build\.NETFramework\v4.7.2'
if (-not (Test-Path -LiteralPath $referenceRoot)) { throw 'Missing pinned .NET Framework 4.7.2 reference assemblies. Run tools\restore-sensors.ps1 first.' }
$references = New-Object System.Collections.Generic.List[string]
foreach ($name in @('mscorlib.dll','System.dll','System.Core.dll','System.Management.dll','System.Web.Extensions.dll')) { $references.Add((Join-Path $referenceRoot $name)) }
$rulesetPath = Join-Path $ProjectRoot 'src\AllRules.ruleset'
if (-not (Test-Path -LiteralPath $rulesetPath)) { throw 'Missing src\AllRules.ruleset; refusing to build without the canonical ruleset.' }
$sourcePaths = New-Object System.Collections.Generic.List[string]
foreach ($folder in @('shared\Sensors','sensors')) { foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot $folder) -File -Filter '*.cs' | Sort-Object Name)) { $sourcePaths.Add($file.FullName) } }
$sourcePaths.Add((Join-Path $ProjectRoot 'sensors\Properties\AssemblyInfo.cs'))
foreach ($source in $sourcePaths) { if (-not (Test-Path -LiteralPath $source)) { throw "Missing backend source '$source'." } }
$analyzerPaths = Read-Analyzers
$compiler = Resolve-Compiler $CompilerPath

$staging = Join-Path $ProjectRoot ('build\sensors\payload-staging-' + [Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($staging)
try
{
$executablePath = Join-Path $staging 'WinMemoryCleaner.Sensors.exe'
$arguments = New-Object System.Collections.Generic.List[string]
foreach ($argument in @('/noconfig','/nostdlib+','/nologo','/langversion:4','/platform:x64','/warn:4','/warnaserror+','/optimize+','/debug:pdbonly','/deterministic+','/define:TRACE','/target:exe')) { $arguments.Add($argument) }
$arguments.Add('/ruleset:' + $rulesetPath)
$arguments.Add('/out:' + $executablePath)
$arguments.Add('/win32manifest:' + (Join-Path $ProjectRoot 'sensors\App.manifest'))
foreach ($reference in $references) { $arguments.Add('/reference:' + $reference) }
foreach ($library in $runtimeLibraries) { $arguments.Add('/reference:' + $library) }
foreach ($analyzer in $analyzerPaths) { $arguments.Add('/analyzer:' + $analyzer) }
foreach ($source in $sourcePaths) { $arguments.Add($source) }
Write-Host ('Compiling ' + $sourcePaths.Count + ' source files with ' + $analyzerPaths.Count + ' analyzers using ' + $compiler)
$argumentArray = $arguments.ToArray()
& $compiler @argumentArray
$compileCode = $LASTEXITCODE
if ($compileCode -ne 0) { throw "The sensor backend did not compile cleanly (exit $compileCode); warnings are errors and the analyzer ruleset was applied." }
if (-not (Test-Path -LiteralPath $executablePath)) { throw 'The sensor backend executable was not produced.' }

# Test code is compiled with the exact compiler, references, sources and analyzer set used above.
# It is never included in the executable payload, and all hardware/installer paths use substitutes.
if ($Tests)
{
    $testAssembly = Join-Path $staging 'WinMemoryCleaner.Sensors.Tests.dll'
    $nunitPath = Join-Path $ProjectRoot 'src\packages\NUnit.2.6.4\lib\nunit.framework.dll'
    if (-not (Test-Path -LiteralPath $nunitPath)) { throw 'Missing NUnit framework for the backend safety suite.' }
    $testArguments = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @('/noconfig','/nostdlib+','/nologo','/langversion:4','/platform:x64','/warn:4','/warnaserror+','/nowarn:3001,3003','/optimize+','/deterministic+','/define:TRACE','/target:library')) { $testArguments.Add($argument) }
    $testArguments.Add('/ruleset:' + $rulesetPath)
    $testArguments.Add('/out:' + $testAssembly)
    foreach ($reference in $references) { $testArguments.Add('/reference:' + $reference) }
    foreach ($library in $runtimeLibraries) { $testArguments.Add('/reference:' + $library) }
    $testArguments.Add('/reference:' + $nunitPath)
    foreach ($analyzer in $analyzerPaths) { $testArguments.Add('/analyzer:' + $analyzer) }
    foreach ($source in $sourcePaths) { $testArguments.Add($source) }
    foreach ($test in @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'sensors\Test') -File -Filter '*.cs' | Sort-Object Name)) { $testArguments.Add($test.FullName) }
    $testArgumentArray = $testArguments.ToArray()
    Write-Host ('Compiling backend safety tests with the same ' + $analyzerPaths.Count + ' analyzers')
    & $compiler @testArgumentArray
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $testAssembly)) { throw 'The backend safety tests did not compile with the complete analyzer set.' }
    $testDirectory = Join-Path $ProjectRoot 'build\sensors\tests'
    [void][System.IO.Directory]::CreateDirectory($testDirectory)
    [System.IO.File]::Copy($testAssembly,(Join-Path $testDirectory 'WinMemoryCleaner.Sensors.Tests.dll'),$true)
    [System.IO.File]::Copy((Join-Path $ProjectRoot 'sensors\App.config'),(Join-Path $testDirectory 'WinMemoryCleaner.Sensors.Tests.dll.config'),$true)
    [System.IO.File]::Copy($nunitPath,(Join-Path $testDirectory 'nunit.framework.dll'),$true)
    foreach ($library in $runtimeLibraries) { [System.IO.File]::Copy($library,(Join-Path $testDirectory ([System.IO.Path]::GetFileName($library))),$true) }
}

$staged = New-Object System.Collections.Generic.List[object]
$staged.Add([pscustomobject]@{ Path = 'WinMemoryCleaner.Sensors.exe'; Source = $executablePath })
$configPath = Join-Path $staging 'WinMemoryCleaner.Sensors.exe.config'
[System.IO.File]::WriteAllBytes($configPath,[System.IO.File]::ReadAllBytes((Join-Path $ProjectRoot 'sensors\App.config')))
$staged.Add([pscustomobject]@{ Path = 'WinMemoryCleaner.Sensors.exe.config'; Source = $configPath })
foreach ($library in $runtimeLibraries) { $staged.Add([pscustomobject]@{ Path = [System.IO.Path]::GetFileName($library); Source = $library }) }
foreach ($input in $inputFiles) { $staged.Add([pscustomobject]@{ Path = $input.Name; Source = $input.Source }) }

$analyzerInfo = New-Object System.Collections.Generic.List[object]
foreach ($analyzer in $analyzerPaths) { $analyzerInfo.Add([pscustomobject]@{ name = [System.IO.Path]::GetFileName($analyzer); sha256 = Get-Sha256 $analyzer }) }
$sourceInfo = New-Object System.Collections.Generic.List[object]
foreach ($source in $sourcePaths) { $sourceInfo.Add([pscustomobject]@{ path = $source.Substring($ProjectRoot.Length + 1); sha256 = Get-Sha256 $source }) }
$referenceInfo = New-Object System.Collections.Generic.List[object]
foreach ($reference in $references) { $referenceInfo.Add([pscustomobject]@{ name = [System.IO.Path]::GetFileName($reference); sha256 = Get-Sha256 $reference }) }
$compilerVersion = (Get-Item -LiteralPath $compiler).VersionInfo.ProductVersion
$rulesetHash = Get-Sha256 $rulesetPath
$runtimeLibraryInfo = New-Object System.Collections.Generic.List[object]
foreach ($asset in $Lock.assets) { $runtimeLibraryInfo.Add([pscustomobject]@{ name = $asset.name; sha256 = $asset.sha256 }) }
$buildInfo = [pscustomobject]@{
    payloadVersion = $PayloadVersion
    protocol = 1
    compiler = [pscustomobject]@{ name = [System.IO.Path]::GetFileName($compiler); productVersion = $compilerVersion }
    ruleset = [pscustomobject]@{ path = 'src\AllRules.ruleset'; sha256 = $rulesetHash }
    analyzers = $analyzerInfo
    references = $referenceInfo
    sources = $sourceInfo
    runtimeLibraries = $runtimeLibraryInfo
}
$buildInfoPath = Join-Path $staging 'sensors-build-info.json'
[System.IO.File]::WriteAllBytes($buildInfoPath,(New-Object System.Text.UTF8Encoding($false)).GetBytes(($buildInfo | ConvertTo-Json -Depth 6)))
$staged.Add([pscustomobject]@{ Path = 'sensors-build-info.json'; Source = $buildInfoPath })

$files = New-Object System.Collections.Generic.List[object]
foreach ($entry in $staged)
{
    $bytes = [System.IO.File]::ReadAllBytes($entry.Source)
    $hash = Get-Sha256 $entry.Source
    $resource = 'WinMemoryCleaner.Sensors.Payload.' + $entry.Path + '.gz'
    $compressed = Join-Path $staging ($entry.Path + '.gz')
    $output = [System.IO.File]::Create($compressed)
    try
    {
        $gzip = New-Object System.IO.Compression.GZipStream($output,[System.IO.Compression.CompressionMode]::Compress,$true)
        try { $gzip.Write($bytes,0,$bytes.Length) } finally { $gzip.Dispose() }
    }
    finally { $output.Dispose() }
    $files.Add([pscustomobject]@{ path = $entry.Path; length = $bytes.Length; sha256 = $hash; resource = $resource })
}
$manifestPath = Join-Path $staging 'sensors-payload.manifest.json'
$manifest = [pscustomobject]@{ version = $PayloadVersion; protocol = 1; files = $files }
[System.IO.File]::WriteAllBytes($manifestPath,(New-Object System.Text.UTF8Encoding($false)).GetBytes(($manifest | ConvertTo-Json -Depth 4)))

foreach ($file in $files)
{
    $restored = Join-Path $staging ('verify-' + $file.path)
    $input = [System.IO.File]::OpenRead((Join-Path $staging ($file.path + '.gz')))
    try
    {
        $gzip = New-Object System.IO.Compression.GZipStream($input,[System.IO.Compression.CompressionMode]::Decompress,$true)
        try { $output = [System.IO.File]::Create($restored); try { $gzip.CopyTo($output) } finally { $output.Dispose() } } finally { $gzip.Dispose() }
    }
    finally { $input.Dispose() }
    $info = Get-Item -LiteralPath $restored
    if ($info.Length -ne $file.length) { throw ('Payload round trip length mismatch for ' + $file.path) }
    if ((Get-Sha256 $restored) -ne $file.sha256) { throw ('Payload round trip hash mismatch for ' + $file.path) }
    [System.IO.File]::Delete($restored)
}
foreach ($required in @('WinMemoryCleaner.Sensors.exe','WinMemoryCleaner.Sensors.exe.config','LibreHardwareMonitorLib.dll','PawnIO_setup.exe'))
{
    $match = @($files | Where-Object { $_.path -eq $required })
    if ($match.Count -ne 1) { throw "The payload is missing required file '$required'." }
}

if (Test-Path -LiteralPath $OutputDirectory)
{
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch '^(.*\.gz|sensors-payload\.manifest\.json)$' }).Count -ne 0) { throw "Refusing to replace unexpected files in '$OutputDirectory'." }
    [System.IO.Directory]::Delete($OutputDirectory,$true)
}
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)
foreach ($generated in @(Get-ChildItem -LiteralPath $staging -File -Filter '*.gz')) { [System.IO.File]::Move($generated.FullName,(Join-Path $OutputDirectory $generated.Name)) }
[System.IO.File]::Move($manifestPath,(Join-Path $OutputDirectory 'sensors-payload.manifest.json'))
}
finally
{
    if ([System.IO.Directory]::Exists($staging)) { [System.IO.Directory]::Delete($staging,$true) }
}

[pscustomobject]@{
    payloadVersion = $PayloadVersion
    compiler = (Get-Item -LiteralPath $compiler).VersionInfo.ProductVersion
    analyzers = $analyzerPaths.Count
    files = $files.Count
    payloadDirectory = $OutputDirectory
    backendTestsBuilt = [bool]$Tests
    installerExecuted = $false
} | ConvertTo-Json
