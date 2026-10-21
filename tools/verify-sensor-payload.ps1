<#
.SYNOPSIS
Verifies an executable's embedded sensor payload against the same build inputs without executing it.
.DESCRIPTION
Run with Windows PowerShell (.NET Framework). ReflectionOnlyLoad reads metadata/resources only:
no application startup, backend, hardware or installer is invoked, and no user cache is touched.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [string]$PayloadDirectory='',
    [string]$ReportPath=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -gt 5){throw 'Use Windows PowerShell for reflection-only payload verification.'}
$repo=Split-Path -Parent $PSScriptRoot
if(-not $PayloadDirectory){$PayloadDirectory=Join-Path $repo 'build\sensors\payload'}
$Executable=(Get-Item -LiteralPath $Executable).FullName
$PayloadDirectory=(Get-Item -LiteralPath $PayloadDirectory).FullName
$prefix='WinMemoryCleaner.Sensors.Payload.'
$manifestName=$prefix+'manifest.json'
$manifestPath=Join-Path $PayloadDirectory 'sensors-payload.manifest.json'
$lockPath=Join-Path $repo 'sensors\dependencies.lock.json'
$lock=Get-Content -Raw -Encoding UTF8 -LiteralPath $lockPath | ConvertFrom-Json

function Hash-Bytes([byte[]]$Bytes)
{
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-','').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function Hash-File([string]$Path)
{
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Read-Bounded([IO.Stream]$Stream,[long]$Maximum)
{
    $memory=New-Object IO.MemoryStream
    try
    {
        $buffer=New-Object byte[] 8192
        while(($count=$Stream.Read($buffer,0,$buffer.Length)) -ne 0)
        {
            if($memory.Length+$count -gt $Maximum){throw 'Sensor resource exceeds its declared bound.'}
            $memory.Write($buffer,0,$count)
        }
        return ,$memory.ToArray()
    }
    finally { $memory.Dispose() }
}
function Assert-FileBytes([byte[]]$Bytes,[string]$Path,[string]$Label)
{
    if((Get-Item -LiteralPath $Path).Length -ne $Bytes.Length -or (Hash-File $Path) -ne (Hash-Bytes $Bytes))
    { throw ('Same-build input mismatch: '+$Label) }
}

if((Get-Item -LiteralPath $Executable).Length -gt 67108864){throw 'Executable exceeds the verification bound.'}
$assembly=[Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($Executable))
if(-not $assembly.ReflectionOnly){throw 'Executable was not loaded reflection-only.'}
$mainVersion=$assembly.GetName().Version.ToString()
$stream=$assembly.GetManifestResourceStream($manifestName)
if($null -eq $stream){throw 'The executable is missing its sensor manifest.'}
try { $manifestBytes=Read-Bounded $stream 262144 } finally { $stream.Dispose() }
Assert-FileBytes $manifestBytes $manifestPath 'manifest'
$manifest=([Text.UTF8Encoding]::new($false,$true)).GetString($manifestBytes) | ConvertFrom-Json
if((@($manifest.PSObject.Properties.Name|Sort-Object) -join ',') -cne 'files,protocol,version' -or $manifest.version -cne 'lhm-0.9.6-p1' -or $manifest.protocol -ne 1)
{throw 'Unsupported sensor payload manifest.'}
$expectedCount=@($lock.assets).Count+@($lock.licenses).Count+6
if(@($manifest.files).Count -ne $expectedCount -or $expectedCount -gt 64){throw 'Sensor payload file count differs from the dependency lock.'}
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$expectedResources=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
[void]$expectedResources.Add($manifestName)
$verified=New-Object 'System.Collections.Generic.List[object]'
$backendVersion=$null
$buildInfo=$null
$total=0L
foreach($file in $manifest.files)
{
    if((@($file.PSObject.Properties.Name|Sort-Object) -join ',') -cne 'length,path,resource,sha256' -or $file.path -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or -not $seen.Add([string]$file.path))
    {throw 'Invalid or duplicate sensor payload path.'}
    if($file.resource -cne ($prefix+$file.path+'.gz') -or $file.sha256 -cnotmatch '^[0-9a-f]{64}$' -or [long]$file.length -lt 1 -or [long]$file.length -gt 16777216)
    {throw ('Invalid sensor payload entry: '+$file.path)}
    $total+=[long]$file.length
    if($total -gt 67108864){throw 'Sensor payload total exceeds its verification bound.'}
    [void]$expectedResources.Add([string]$file.resource)
    $stream=$assembly.GetManifestResourceStream([string]$file.resource)
    if($null -eq $stream){throw ('Missing embedded resource: '+$file.path)}
    try { $compressed=Read-Bounded $stream 16777216 } finally { $stream.Dispose() }
    Assert-FileBytes $compressed (Join-Path $PayloadDirectory ($file.path+'.gz')) ([string]$file.path+' compressed resource')
    $memory=[IO.MemoryStream]::new($compressed,$false)
    try
    {
        $gzip=[IO.Compression.GZipStream]::new($memory,[IO.Compression.CompressionMode]::Decompress,$true)
        try { $bytes=Read-Bounded $gzip ([long]$file.length) } finally { $gzip.Dispose() }
    }
    finally { $memory.Dispose() }
    if($bytes.Length -ne [long]$file.length -or (Hash-Bytes $bytes) -cne [string]$file.sha256)
    {throw ('Embedded file length/hash mismatch: '+$file.path)}
    switch -Exact ($file.path)
    {
        'WinMemoryCleaner.Sensors.exe' {
            $backend=[Reflection.Assembly]::ReflectionOnlyLoad($bytes)
            $backendVersion=$backend.GetName().Version.ToString()
            if($backend.GetName().Name -cne 'WinMemoryCleaner.Sensors' -or $backendVersion -cne $mainVersion){throw 'Main/backend assembly versions differ.'}
        }
        'WinMemoryCleaner.Sensors.exe.config' { Assert-FileBytes $bytes (Join-Path $repo 'sensors\App.config') 'backend configuration' }
        'sensors-dependencies.lock.json' { Assert-FileBytes $bytes $lockPath 'dependency lock' }
        'THIRD-PARTY-NOTICES.md' { Assert-FileBytes $bytes (Join-Path $repo 'sensors\THIRD-PARTY-NOTICES.md') 'third-party notice' }
        'sensors-build-info.json' { $buildInfo=([Text.UTF8Encoding]::new($false,$true)).GetString($bytes)|ConvertFrom-Json }
        default {
            $pinned=@($lock.assets|Where-Object {$_.name -ceq $file.path})
            $folder='build\sensors\lib'
            if($pinned.Count -eq 0){$pinned=@($lock.licenses|Where-Object {$_.name -ceq $file.path});$folder='build\sensors\inputs'}
            if($pinned.Count -eq 0 -and $lock.installer.name -ceq $file.path){$pinned=@($lock.installer);$folder='build\sensors\inputs'}
            if($pinned.Count -ne 1 -or [long]$pinned[0].bytes -ne $file.length -or $pinned[0].sha256 -cne $file.sha256)
            {throw ('File is not pinned by the dependency lock: '+$file.path)}
            Assert-FileBytes $bytes (Join-Path $repo ($folder+'\'+$file.path)) ([string]$file.path+' pinned input')
        }
    }
    $verified.Add([pscustomobject]@{path=$file.path;length=$file.length;sha256=$file.sha256})
}
$actualResources=@($assembly.GetManifestResourceNames()|Where-Object {$_.StartsWith($prefix,[StringComparison]::Ordinal)})
if($actualResources.Count -ne $expectedResources.Count -or @($actualResources|Where-Object {-not $expectedResources.Contains($_)}).Count -ne 0)
{throw 'Executable has missing or unexpected sensor resources.'}
$staged=@(Get-ChildItem -LiteralPath $PayloadDirectory -Force)
if($staged.Count -ne $expectedResources.Count -or @($staged|Where-Object {$_.PSIsContainer -or ($_.Name -cne 'sensors-payload.manifest.json' -and -not $seen.Contains(($_.Name -replace '\.gz$',''))) -or ($_.Name -cne 'sensors-payload.manifest.json' -and $_.Extension -cne '.gz')}).Count -ne 0)
{throw 'Staged payload has unexpected files.'}
if($null -eq $backendVersion -or $null -eq $buildInfo){throw 'Sensor payload is missing build metadata.'}
if($buildInfo.payloadVersion -cne $manifest.version -or $buildInfo.protocol -ne 1 -or $buildInfo.compiler.name -cne 'csc.exe' -or $buildInfo.compiler.productVersion -notmatch '^\d+\.' -or $buildInfo.ruleset.sha256 -cne (Hash-File (Join-Path $repo 'src\AllRules.ruleset')))
{throw 'Sensor build provenance does not match this build.'}
$sourcePaths=@()
foreach($folder in @('shared\Sensors','sensors')){$sourcePaths+=@(Get-ChildItem -LiteralPath (Join-Path $repo $folder) -File -Filter '*.cs'|ForEach-Object {$_.FullName.Substring($repo.Length+1)})}
$sourcePaths+='sensors\Properties\AssemblyInfo.cs'
$sourceSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if(@($buildInfo.sources).Count -ne $sourcePaths.Count){throw 'Sensor source provenance count differs.'}
foreach($source in $buildInfo.sources)
{
    if($sourcePaths -notcontains $source.path -or -not $sourceSet.Add([string]$source.path) -or $source.sha256 -cne (Hash-File (Join-Path $repo $source.path)))
    {throw ('Sensor source provenance mismatch: '+$source.path)}
}
$analyzerPaths=@()
foreach($line in Get-Content -Encoding UTF8 -LiteralPath (Join-Path $repo 'sensors\Analyzers.props'))
{if($line -match '<Analyzer Include="\$\(SensorAnalyzerPackages\)([^"]+)"'){$analyzerPaths+=Join-Path $repo ('src\packages\'+$Matches[1])}}
if($analyzerPaths.Count -ne 9 -or @($buildInfo.analyzers).Count -ne 9){throw 'Complete sensor analyzer provenance is required.'}
foreach($path in $analyzerPaths)
{
    $entry=@($buildInfo.analyzers|Where-Object {$_.name -ceq [IO.Path]::GetFileName($path)})
    if($entry.Count -ne 1 -or $entry[0].sha256 -cne (Hash-File $path)){throw ('Sensor analyzer provenance mismatch: '+$path)}
}
$report=[pscustomobject]@{mainVersion=$mainVersion;backendVersion=$backendVersion;payloadVersion=$manifest.version;manifestSha256=(Hash-Bytes $manifestBytes);files=$verified.Count;resources=$actualResources.Count;sourceFiles=$sourcePaths.Count;analyzers=$analyzerPaths.Count;reflectionOnly=$true;installerExecuted=$false;hardwareOpened=$false;verifiedFiles=$verified.ToArray()}
$json=$report|ConvertTo-Json -Depth 5
if($ReportPath)
{
    $ReportPath=[IO.Path]::GetFullPath($ReportPath)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath))
    [IO.File]::WriteAllText($ReportPath,$json,[Text.UTF8Encoding]::new($false))
}
$json
