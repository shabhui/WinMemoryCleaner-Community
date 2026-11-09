# Same-toolchain, isolated, deterministic refresh allocation/draw comparison.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BaselineAssembly,
    [Parameter(Mandatory=$true)][string]$OptimizedAssembly,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$HarnessDirectory=$env:PI_SCRATCH_DIR,
    [ValidateRange(1,20)][int]$Trials=3
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$BaselineAssembly=(Resolve-Path -LiteralPath $BaselineAssembly).Path
$OptimizedAssembly=(Resolve-Path -LiteralPath $OptimizedAssembly).Path
if(-not $HarnessDirectory){throw 'Specify a session scratch directory.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a fresh output directory; refusing to mix runs.'}
[void](New-Item -ItemType Directory -Force -Path $OutputDirectory,$HarnessDirectory)
$OutputDirectory=(Resolve-Path -LiteralPath $OutputDirectory).Path
$ref=Join-Path $repo 'src\packages\Microsoft.NETFramework.ReferenceAssemblies.net40.1.0.3\build\.NETFramework\v4.0'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source=Join-Path $PSScriptRoot 'RefreshHotPathProbe.cs'
$harness=Join-Path $HarnessDirectory 'RefreshHotPathProbe.exe'
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll') | ForEach-Object {'/reference:'+(Join-Path $ref $_)}
& $compiler /noconfig /nostdlib+ /langversion:4 /warn:4 /warnaserror+ /optimize+ /target:exe ('/out:'+$harness) ('/reference:'+$BaselineAssembly) $references $source *> (Join-Path $OutputDirectory 'probe-build.log')
if($LASTEXITCODE -ne 0){throw ('Probe compilation failed: '+(Join-Path $OutputDirectory 'probe-build.log'))}
[IO.File]::WriteAllText(($harness+'.config'),'<?xml version="1.0"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup></configuration>')
[ordered]@{
    createdAt=(Get-Date).ToString('o'); trials=$Trials; compiler=$compiler
    compilerVersion=(Get-Item -LiteralPath $compiler).VersionInfo.FileVersion
    probeSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    baseline=$BaselineAssembly; baselineSha256=(Get-FileHash -LiteralPath $BaselineAssembly -Algorithm SHA256).Hash
    optimized=$OptimizedAssembly; optimizedSha256=(Get-FileHash -LiteralPath $OptimizedAssembly -Algorithm SHA256).Hash
    sequential=$true; settings='CommunityTestBuild only'; scope='Synthetic hot paths, not whole-process footprint acceptance'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'conditions.json') -Encoding UTF8
for($trial=1;$trial -le $Trials;$trial++) {
    foreach($variant in @('baseline','optimized')) {
        $assembly=if($variant -eq 'baseline'){$BaselineAssembly}else{$OptimizedAssembly}
        $prefix=Join-Path $OutputDirectory ($variant+'-'+$trial)
        $info=New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName=$harness; $info.Arguments='"{0}" "{1}"' -f $assembly,($prefix+'.json')
        $info.UseShellExecute=$false; $info.CreateNoWindow=$true
        $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
        $process=[System.Diagnostics.Process]::Start($info)
        try {
            if(-not $process.WaitForExit(60000)) {
                # This exact process instance was created by this invocation.
                $process.Kill(); $process.WaitForExit(); throw ('Our probe timed out: '+$prefix)
            }
            $process.StandardOutput.ReadToEnd() | Set-Content -LiteralPath ($prefix+'.stdout.log') -Encoding UTF8
            $process.StandardError.ReadToEnd() | Set-Content -LiteralPath ($prefix+'.stderr.log') -Encoding UTF8
            if($process.ExitCode -ne 0){throw ('Probe failed: '+$prefix+'.stderr.log')}
        }
        finally { $process.Dispose() }
        Write-Output ($variant+' trial '+$trial+': '+$prefix+'.json')
    }
}
