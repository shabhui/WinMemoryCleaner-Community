# Real WPF windows, disposable HKLM redirection and machine-normalized sampling.
# Run trials sequentially. This never closes another WMC instance or cleans global memory.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$AssemblyPath,
    [string]$ApplicationXamlPath,
    [ValidateSet('tray','main','process','overlay','settings','minimum','light','compact','stress','retention','interaction')]
    [string[]]$Modes = @('tray','main','process','overlay'),
    [int]$WarmupSeconds = 30,
    [int]$MeasurementSeconds = 60,
    [int]$Trials = 3,
    [string]$OutputDirectory,
    [string]$HarnessDirectory = $env:PI_SCRATCH_DIR,
    [switch]$CaptureScreenshots
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
if (-not $ApplicationXamlPath) { $ApplicationXamlPath=Join-Path $repo 'src\App.xaml' }
$ApplicationXamlPath=(Resolve-Path -LiteralPath $ApplicationXamlPath).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'build\monitoring-verification\current' }
if (-not $HarnessDirectory) { throw 'Set HarnessDirectory to a temporary directory.' }
if ($WarmupSeconds -lt 0 -or $MeasurementSeconds -lt 1 -or $Trials -lt 1) { throw 'Invalid timing/trial settings.' }
[void](New-Item -ItemType Directory -Force -Path $OutputDirectory,$HarnessDirectory)
$ref = Join-Path $repo 'src\packages\Microsoft.NETFramework.ReferenceAssemblies.net40.1.0.3\build\.NETFramework\v4.0'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path $HarnessDirectory 'MonitoringHarness.exe'
$references = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Xml.dll','System.Xml.Linq.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:'+(Join-Path $ref $_) }
& $compiler /noconfig /nostdlib+ /langversion:4 /warnaserror+ /target:exe ('/out:'+$harness) ('/reference:'+$AssemblyPath) $references (Join-Path $PSScriptRoot 'MonitoringHarness.cs') (Join-Path $PSScriptRoot 'MonitoringInteraction.cs') *> (Join-Path $OutputDirectory 'harness-build.log')
if ($LASTEXITCODE -ne 0) { throw ('Harness build failed: '+(Join-Path $OutputDirectory 'harness-build.log')) }
@'
<?xml version="1.0"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.0"/></startup></configuration>
'@ | Set-Content -LiteralPath ($harness+'.config') -Encoding UTF8
$conditions = [ordered]@{
    assembly=$AssemblyPath; assemblyBytes=(Get-Item -LiteralPath $AssemblyPath).Length
    assemblySha256=(Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash
    applicationXaml=$ApplicationXamlPath; applicationXamlSha256=(Get-FileHash -LiteralPath $ApplicationXamlPath -Algorithm SHA256).Hash
    os=[Environment]::OSVersion.VersionString; logicalProcessors=[Environment]::ProcessorCount
    warmupSeconds=$WarmupSeconds; measurementSeconds=$MeasurementSeconds; trials=$Trials
    captureScreenshots=[bool]$CaptureScreenshots; interactionCaptures=$true
    harnessSources=@('MonitoringHarness.cs','MonitoringInteraction.cs','verify-monitoring.ps1') | ForEach-Object { [ordered]@{file=$_; sha256=(Get-FileHash (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash} }
    sequential=$true; clock='Stopwatch'; cpu='Process.TotalProcessorTime / elapsed / logicalProcessors * 100'
    memory='Process.PrivateMemorySize64 (working set separately)'; isolation='Disposable HKCU subtree redirected to HKLM in harness process only'
    settings='zh-Hans; dark (except light capture); 14pt; no auto-clean/update/hotkey; tray memory on; monitor interval default 2s; overlay only in overlay mode'
}
$conditions | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'conditions.json') -Encoding UTF8
$rows = @()
foreach ($mode in $Modes) {
    for ($trial=1; $trial -le $Trials; $trial++) {
        $dir = Join-Path $OutputDirectory ($mode+'-'+$trial)
        if (Test-Path -LiteralPath (Join-Path $dir 'summary.json')) { throw ('Choose a fresh output directory; refusing to mix trials: '+$dir) }
        [void](New-Item -ItemType Directory -Force -Path $dir)
        Write-Output ("{0:o} {1} trial {2}: warm {3}s / sample {4}s" -f (Get-Date),$mode,$trial,$WarmupSeconds,$MeasurementSeconds)
        $arguments = '"{0}" {1} "{2}" {3} {4} {5} "{6}" {7}' -f $AssemblyPath,$mode,$dir,$WarmupSeconds,$MeasurementSeconds,$trial,$ApplicationXamlPath,[int][bool]$CaptureScreenshots
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName=$harness; $info.Arguments=$arguments; $info.UseShellExecute=$false
        $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
        $process = [System.Diagnostics.Process]::Start($info)
        if (-not $process.WaitForExit([Math]::Max(240,($WarmupSeconds+$MeasurementSeconds+90))*1000)) {
            # Only the process returned by OUR Start-Process is eligible for shutdown.
            $process.Kill(); $process.WaitForExit(); throw 'Our verification harness timed out.'
        }
        $code=$process.ExitCode
        $process.StandardOutput.ReadToEnd() | Set-Content -LiteralPath (Join-Path $dir 'stdout.log') -Encoding UTF8
        $process.StandardError.ReadToEnd() | Set-Content -LiteralPath (Join-Path $dir 'stderr.log') -Encoding UTF8
        $process.Dispose()
        $keyFile = Join-Path $dir 'isolated-registry-path.txt'
        if (Test-Path -LiteralPath $keyFile) {
            $key=(Get-Content -LiteralPath $keyFile -Raw).Trim()
            if ($key -notmatch '^Software\\WinMemoryCleaner\.Verification\\[0-9a-f]{32}$') { throw 'Unsafe registry cleanup path.' }
            $keyPath='HKCU:\'+$key
            if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath -Recurse -Force }
        }
        if ($code -ne 0) { throw ('Harness failed ('+$code+'): '+$dir) }
        $rows += Get-Content -LiteralPath (Join-Path $dir 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    }
}
$rows | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'trials.json') -Encoding UTF8
function Median($values) { $v=@($values | Sort-Object); if ($v.Count % 2) { return $v[[int][Math]::Floor($v.Count/2)] }; return ($v[$v.Count/2-1]+$v[$v.Count/2])/2 }
$medians = foreach ($mode in @($Modes | Where-Object { $_ -ne 'interaction' })) {
    $state=@($rows | Where-Object { $_.mode -eq $mode })
    [pscustomobject]@{
        mode=$mode; trials=$state.Count
        privateBytes=(Median @($state | ForEach-Object {$_.medianPrivateBytes}))
        meanCpuMachinePercent=(Median @($state | ForEach-Object {$_.meanCpuMachinePercent}))
        dispatcherP95Ms=(Median @($state | ForEach-Object {$_.dispatcherP95Ms}))
        maxPrivateGrowthBytes=(@($state | ForEach-Object {$_.privateGrowthBytes} | Sort-Object -Descending)[0])
    }
}
$medians | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'medians.json') -Encoding UTF8
$medians | Format-Table -AutoSize
