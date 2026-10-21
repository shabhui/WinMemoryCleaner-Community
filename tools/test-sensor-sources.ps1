# Behavioral checks of the real source producer; uses genuine pinned archives, never hardware.
param([string]$ProjectRoot, [string]$SourceCache, [string]$ReportPath)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
if (-not $SourceCache) { $SourceCache = Join-Path $ProjectRoot 'build\sensors\source-archives' }
$tool = Join-Path $ProjectRoot 'tools\package-sensor-sources.ps1'
$temporaryRoot = if ($env:PI_SCRATCH_DIR) { $env:PI_SCRATCH_DIR } elseif ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$root = Join-Path $temporaryRoot ('wmc-source-test-' + [Guid]::NewGuid().ToString('N'))
# Expectations are literal, independently verified upstream archive/license hashes, not producer output.
$pins = [ordered]@{
    'PawnIO-e1043d7750f501b6b0d3ac0a71bd5b9406cd17b6.tar.gz' = 'c0356daa5418950e17cfd2c830d62b916d5c0a97bcca2e27622fadae4605fc75'
    'PawnPP-e64e4c37b2d8ba0d8ee57205faf8183aee12c438.tar.gz' = 'a7a4c76ea1ca5a4291e805ef8490fc2cdd93ba69bc1e5f25ec50eb5a8f892962'
    'PawnIO.Modules-e12a858d952461ee2e919897cacbff7f905fe370.tar.gz' = 'e82ef4c5c06461c74685fa60ebf28297e4830876f8bbf388187d7aa063bcf096'
    'PawnIO-COPYING.txt' = '8177f97513213526df2cf6184d8ff986c675afb514d4e68a404010521b880643'
    'PawnIO-LGPL-2.1.txt' = 'dc626520dcd53a22f727af3ee42c770e56c97a64fe3adb063799d8ab032fe551'
    'PawnIO-Linking-Exception.txt' = '80ff9b48b780c0d4d116c93707974e960a577d5b71a76a17cafe05b0b2f143c8'
    'MPL-2.0.txt' = '1f256ecad192880510e84ad60474eab7589218784b9a50bc7ceee34c2b91f1d5'
}
if (-not (Test-Path -LiteralPath $tool)) { throw 'ASSERT: corresponding-source producer is missing.' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[void][IO.Directory]::CreateDirectory($root)
function Invoke-Producer([string]$Cache, [string]$Output, [string]$Log, [string]$Report) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $tool, '-ProjectRoot', $ProjectRoot, '-SourceCache', $Cache, '-Offline', '-OutputPath', $Output)
    if ($Report) { $arguments += @('-ReportPath', $Report) }
    $ErrorActionPreference = 'Continue'
    & powershell.exe @arguments *> $Log
    return $LASTEXITCODE
}
try {
    $output = Join-Path $root 'good.zip'; $report = Join-Path $root 'report.json'; $log = Join-Path $root 'good.log'
    if ((Invoke-Producer $SourceCache $output $log $report) -ne 0) { Get-Content -LiteralPath $log -Tail 16; throw 'ASSERT: genuine locked archives did not produce a source bundle.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($output)
    try {
        if ($zip.Entries.Count -ne 10) { throw 'ASSERT: unexpected/missing source bundle entries.' }
        foreach ($pin in $pins.GetEnumerator()) {
            $entry = $zip.GetEntry($pin.Key)
            if (-not $entry) { throw ('ASSERT: missing source/submodule/license: ' + $pin.Key) }
            $stream = $entry.Open(); $algorithm = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $algorithm.Dispose(); $stream.Dispose() }
            if ($actual -ne $pin.Value) { throw ('ASSERT: altered source/license bytes: ' + $pin.Key) }
        }
        foreach ($name in @('README.md', 'sources.lock.json', 'THIRD-PARTY-NOTICES.md')) { if (-not $zip.GetEntry($name)) { throw ('ASSERT: missing provenance: ' + $name) } }
    } finally { $zip.Dispose() }
    $r = [IO.File]::ReadAllText($report, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($r.archives -ne 3 -or $r.installerExecuted -ne $false -or $r.hardwareOpened -ne $false -or $r.sha256 -ne (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'ASSERT: incorrect source bundle report.' }
    Write-Output 'VALID_INPUT_BUNDLE_PASS'

    $bad = Join-Path $root 'corrupt'; [void][IO.Directory]::CreateDirectory($bad)
    foreach ($name in @($pins.Keys | Where-Object { $_.EndsWith('.tar.gz') })) { Copy-Item -LiteralPath (Join-Path $SourceCache $name) -Destination (Join-Path $bad $name) }
    $changed = Join-Path $bad 'PawnIO-e1043d7750f501b6b0d3ac0a71bd5b9406cd17b6.tar.gz'
    $bytes = [IO.File]::ReadAllBytes($changed); $bytes[100] = $bytes[100] -bxor 1; [IO.File]::WriteAllBytes($changed, $bytes)
    $refused = Join-Path $root 'refused.zip'; $badLog = Join-Path $root 'corrupt.log'
    $badCode = Invoke-Producer $bad $refused $badLog ''
    if ($badCode -eq 0 -or (Test-Path -LiteralPath $refused) -or [IO.File]::ReadAllText($badLog) -notmatch 'Source archive integrity mismatch') { throw 'ASSERT: same-length corruption was not rejected without publishing a source bundle.' }
    Write-Output 'CORRUPT_INPUT_REJECTED_PASS'
    if ($ReportPath) {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath)))
        [pscustomobject]@{ total = 2; failures = 0; archives = 3; entries = 10; installerExecuted = $false; hardwareOpened = $false } | ConvertTo-Json | Out-File -LiteralPath $ReportPath -Encoding UTF8
    }
} finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
