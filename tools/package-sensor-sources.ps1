# Package fixed corresponding-source archives as data only. No archive member is executed.
param(
    [string]$ProjectRoot,
    [string]$SourceCache,
    [switch]$Offline,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (-not $SourceCache) { $SourceCache = Join-Path $ProjectRoot 'build\sensors\source-archives' }
$SourceCache = [IO.Path]::GetFullPath($SourceCache)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ($ReportPath) { $ReportPath = [IO.Path]::GetFullPath($ReportPath) }
$lockPath = Join-Path $ProjectRoot 'sensors\sources.lock.json'
$lock = [IO.File]::ReadAllText($lockPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$dependencies = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'sensors\dependencies.lock.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
if ($lock.schema -ne 1 -or $lock.payload -ne $dependencies.payload -or @($lock.archives).Count -ne 3) { throw 'Unsupported corresponding-source lock.' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Assert-NoReparse([string]$Path) {
    for ($part = [IO.Path]::GetFullPath($Path); $part; $part = [IO.Path]::GetDirectoryName($part)) {
        if ((Test-Path -LiteralPath $part) -and (([IO.File]::GetAttributes($part) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw "Refusing reparse path: $part" }
    }
}
function Get-BytesHash([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function Assert-PinnedBytes([byte[]]$Bytes, $Pin, [string]$Kind) {
    if ($Bytes.LongLength -ne [long]$Pin.bytes -or -not [String]::Equals((Get-BytesHash $Bytes), [string]$Pin.sha256, [StringComparison]::OrdinalIgnoreCase)) { throw "$Kind integrity mismatch: $($Pin.name)" }
}
function Read-PinnedBytes([string]$Path, $Pin, [string]$Kind) {
    Assert-NoReparse $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing $Kind input: $Path" }
    if ((Get-Item -LiteralPath $Path).Length -ne [long]$Pin.bytes) { throw "$Kind integrity mismatch: $($Pin.name)" }
    $bytes = [IO.File]::ReadAllBytes($Path)
    Assert-PinnedBytes $bytes $Pin $Kind
    return ,$bytes
}
function Get-SourceBytes($Pin) {
    $cached = Join-Path $SourceCache $Pin.name
    if (Test-Path -LiteralPath $cached) { return ,(Read-PinnedBytes $cached $Pin 'Source archive') }
    if ($Offline) { throw "Offline source archive is missing: $($Pin.name)" }
    $client = New-Object Net.WebClient
    try {
        $client.Headers['User-Agent'] = 'WinMemoryCleaner-Community-source-build'
        $bytes = $client.DownloadData($Pin.url)
    } finally { $client.Dispose() }
    Assert-PinnedBytes $bytes $Pin 'Source archive'
    Assert-NoReparse $cached
    [void][IO.Directory]::CreateDirectory($SourceCache)
    $temporary = $cached + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try { [IO.File]::WriteAllBytes($temporary, $bytes); [IO.File]::Move($temporary, $cached) }
    finally { if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) } }
    return ,$bytes
}

Assert-NoReparse $OutputPath
if (Test-Path -LiteralPath $OutputPath) { throw "Refusing to replace an existing source bundle: $OutputPath" }
if ($ReportPath) {
    Assert-NoReparse $ReportPath
    if ([String]::Equals($ReportPath, $OutputPath, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $ReportPath)) { throw 'Source bundle report must be a new, separate file.' }
}
$files = New-Object 'System.Collections.Generic.List[object]'
$names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($pin in $lock.archives) {
    if ($pin.name -notmatch '^[A-Za-z0-9_.-]+\.tar\.gz$' -or $pin.commit -notmatch '^[0-9a-f]{40}$' -or $pin.sha256 -notmatch '^[0-9a-f]{64}$' -or [long]$pin.bytes -le 0 -or [long]$pin.bytes -gt 33554432 -or $pin.url -notmatch '^https://codeload\.github\.com/namazso/[A-Za-z0-9.]+/tar\.gz/[0-9a-f]{40}$' -or -not $names.Add($pin.name)) { throw 'Unsafe source archive identity in lock.' }
    $files.Add([pscustomobject]@{ name = $pin.name; bytes = (Get-SourceBytes $pin) })
}
foreach ($name in @('PawnIO-COPYING.txt', 'PawnIO-LGPL-2.1.txt', 'PawnIO-Linking-Exception.txt', 'MPL-2.0.txt')) {
    $pins = @($dependencies.licenses | Where-Object { $_.name -eq $name })
    if ($pins.Count -ne 1) { throw "Missing unique locked license: $name" }
    $files.Add([pscustomobject]@{ name = $name; bytes = (Read-PinnedBytes (Join-Path $ProjectRoot ('build\sensors\inputs\' + $name)) $pins[0] 'License') })
}
foreach ($document in @(
    @{ name = 'README.md'; path = 'sensors\SOURCE-README.md' },
    @{ name = 'sources.lock.json'; path = 'sensors\sources.lock.json' },
    @{ name = 'THIRD-PARTY-NOTICES.md'; path = 'sensors\THIRD-PARTY-NOTICES.md' }
)) {
    $path = Join-Path $ProjectRoot $document.path
    Assert-NoReparse $path
    $files.Add([pscustomobject]@{ name = $document.name; bytes = [IO.File]::ReadAllBytes($path) })
}

# Hold verified byte arrays, not mutable file names, until the ZIP is committed.
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
$temporaryZip = $OutputPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$temporaryReport = $null
try {
    $zip = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $entry = $zip.CreateEntry($file.name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::Parse('1980-01-01T00:00:00Z', [Globalization.CultureInfo]::InvariantCulture)
            $stream = $entry.Open()
            try { $stream.Write($file.bytes, 0, $file.bytes.Length) }
            finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    $report = [pscustomobject]@{
        schema = 1; payload = $lock.payload; archives = $lock.archives.Count; entries = $files.Count
        bundle = [IO.Path]::GetFileName($OutputPath); bytes = (Get-Item -LiteralPath $temporaryZip).Length
        sha256 = (Get-FileHash -LiteralPath $temporaryZip -Algorithm SHA256).Hash.ToLowerInvariant()
        sources = $lock.archives; installerExecuted = $false; hardwareOpened = $false
    }
    $json = $report | ConvertTo-Json -Depth 8
    if ($ReportPath) {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath))
        $temporaryReport = $ReportPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        [IO.File]::WriteAllText($temporaryReport, $json, (New-Object Text.UTF8Encoding($false)))
    }
    [IO.File]::Move($temporaryZip, $OutputPath)
    if ($ReportPath) { [IO.File]::Move($temporaryReport, $ReportPath) }
    Write-Output $json
} finally {
    foreach ($temporary in @($temporaryZip, $temporaryReport)) { if ($temporary -and (Test-Path -LiteralPath $temporary)) { [IO.File]::Delete($temporary) } }
}
