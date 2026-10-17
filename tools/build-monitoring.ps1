# Same-toolchain builds for the monitoring acceptance checks. No packaging or user-process shutdown.
[CmdletBinding()]
param(
    [ValidateSet('Baseline','Backend','Debug','Release','Test')][string]$Mode='Release',
    [string]$ScratchDirectory=$env:PI_SCRATCH_DIR
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $ScratchDirectory){throw 'Specify a session scratch directory.'}
$scratch=Join-Path $ScratchDirectory 'wmc-goal'
New-Item -ItemType Directory -Force -Path $scratch,(Join-Path $scratch 'logs') | Out-Null
$ref=Join-Path $repo 'src\packages\Microsoft.NETFramework.ReferenceAssemblies.net40.1.0.3\build\.NETFramework\v4.0'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$msbuild=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
$runnerSource=Join-Path $repo 'src\packages\NUnit.Runners.2.6.4\tools'
$runnerDirectory=Join-Path $scratch 'nunit'
New-Item -ItemType Directory -Force -Path $runnerDirectory | Out-Null
Copy-Item -Path (Join-Path $runnerSource '*') -Destination $runnerDirectory -Recurse -Force
$runner=Join-Path $runnerDirectory 'nunit-console.exe'
foreach($exe in Get-ChildItem -LiteralPath $runnerDirectory -Filter '*.exe') {
    $configuration='<?xml version="1.0"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup><runtime><loadFromRemoteSources enabled="true"/><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><probing privatePath="lib;addins"/></assemblyBinding></runtime></configuration>'
    [IO.File]::WriteAllText(($exe.FullName+'.config'),$configuration)
}
$log=Join-Path $scratch ('logs\'+$Mode.ToLowerInvariant()+'-build.log')
if($Mode -eq 'Backend') {
    $dll=Join-Path $scratch 'Backend.Tests.dll'
    $references=@('mscorlib.dll','System.dll','System.Core.dll','System.Management.dll') | ForEach-Object { '/reference:'+(Join-Path $ref $_) }
    $files=@(Get-ChildItem -LiteralPath (Join-Path $repo 'src\Model\Monitoring') -Filter '*.cs' | Where-Object {$_.Name -ne 'ProcessRow.cs'} | ForEach-Object {$_.FullName})
    $files+=@(Get-ChildItem -LiteralPath (Join-Path $repo 'src\Service\Monitoring') -Filter '*.cs' | ForEach-Object {$_.FullName})
    $files+=@(Join-Path $repo 'src\Interop\MonitoringNativeMethods.cs')
    $files+=@(Join-Path $repo 'src\Interfaces\IResourceMonitor.cs')
    $files+=@('ResourceMonitoringTests.cs','ProcessActionTests.cs','NativeSamplingTests.cs','AsusTemperatureTests.cs') | ForEach-Object {Join-Path $repo ('src\Test\'+$_)} | Where-Object {Test-Path -LiteralPath $_}
    & $compiler /noconfig /nostdlib+ /langversion:4 /warn:4 /warnaserror+ /target:library ('/out:'+$dll) ('/reference:'+(Join-Path $repo 'src\packages\NUnit.2.6.4\lib\nunit.framework.dll')) $references $files *> $log
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 60;exit $LASTEXITCODE}
    Copy-Item -LiteralPath (Join-Path $repo 'src\packages\NUnit.2.6.4\lib\nunit.framework.dll') -Destination $scratch -Force
    $ErrorActionPreference='Continue'
    & $runner $dll '/framework:net-4.0' '/exclude:Manual,Desktop' /noshadow /nologo ('/work:'+$scratch) ('/xml:'+(Join-Path $scratch 'logs\backend-tests.xml')) *> (Join-Path $scratch 'logs\backend-tests.log')
    $code=$LASTEXITCODE; Get-Content -LiteralPath (Join-Path $scratch 'logs\backend-tests.log') -Tail 45; exit $code
}
$root=$repo
if($Mode -eq 'Baseline') {
    $root=Join-Path $scratch 'baseline'
    if(-not (Test-Path -LiteralPath (Join-Path $root 'src\WinMemoryCleaner.csproj'))) {
        $archive=Join-Path $scratch 'baseline.zip'
        & git -c ('safe.directory='+$repo.Replace('\','/')) -C $repo archive --format=zip ('--output='+$archive) da2cf8b
        if($LASTEXITCODE -ne 0){throw 'Baseline archive failed.'}
        Expand-Archive -LiteralPath $archive -DestinationPath $root -Force
    }
    $junction=Join-Path $root 'src\packages'
    if(-not (Test-Path -LiteralPath $junction)){New-Item -ItemType Junction -Path $junction -Target (Join-Path $repo 'src\packages') | Out-Null}
}
$config=if($Mode -eq 'Debug'){'Debug'}else{'Release'}
# Different output paths leave any existing user instance's executable untouched.
$out=Join-Path $scratch ($Mode.ToLowerInvariant()+'\')
$intermediate=Join-Path $scratch ('obj-'+$Mode.ToLowerInvariant()+'\')
$arguments=@((Join-Path $root 'src\WinMemoryCleaner.csproj'),'/t:Rebuild','/tv:4.0','/m:1','/nr:false','/nologo','/v:minimal',('/p:Configuration='+$config),'/p:Platform=AnyCPU',('/p:FrameworkPathOverride='+$ref),('/p:OutputPath='+$out),('/p:IntermediateOutputPath='+$intermediate),('/p:DocumentationFile='+(Join-Path $out 'WinMemoryCleaner.xml')))
if($Mode -eq 'Test'){$arguments+='/p:CommunityTestBuild=true'}
& $msbuild @arguments *> $log
$code=$LASTEXITCODE; Get-Content -LiteralPath $log -Tail 65
if($code -ne 0){exit $code}
Write-Output ('ASSEMBLY: '+(Join-Path $out 'WinMemoryCleaner.Community.exe'))
Write-Output ('APP XAML: '+(Join-Path $root 'src\App.xaml'))
if($Mode -eq 'Test') {
    $work=Join-Path $scratch 'safe-tests'; New-Item -ItemType Directory -Force -Path $work | Out-Null
    $ErrorActionPreference='Continue'
    & $runner (Join-Path $out 'WinMemoryCleaner.Community.exe') '/framework:net-4.0' '/process:Single' '/domain:None' '/apartment:STA' '/exclude:Manual,Desktop' /labels /noshadow /nologo ('/work:'+$work) ('/xml:'+(Join-Path $scratch 'logs\safe-tests.xml')) *> (Join-Path $scratch 'logs\safe-tests.log')
    $code=$LASTEXITCODE; Get-Content -LiteralPath (Join-Path $scratch 'logs\safe-tests.log') -Tail 70
}
exit $code
