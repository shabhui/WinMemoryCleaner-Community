param([string]$ProjectRoot,[string]$SourceCache,[string]$OutputPath,[switch]$Offline)
$ErrorActionPreference='Stop'
if(-not $ProjectRoot){if(-not $PSScriptRoot){throw 'Run restore-sensors from its file or pass -ProjectRoot.'};$ProjectRoot=Split-Path $PSScriptRoot -Parent}
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot)
if(-not $OutputPath){$OutputPath=Join-Path $ProjectRoot 'build\sensors'}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
$lock=[IO.File]::ReadAllText((Join-Path $ProjectRoot 'sensors\dependencies.lock.json'),[Text.Encoding]::UTF8)|ConvertFrom-Json
if($lock.schema-ne 1-or$lock.payload-ne 'lhm-0.9.6-p1'){throw 'Unsupported sensor dependency lock.'}
[Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol-bor[Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-NoReparse([string]$Path){
    for($part=[IO.Path]::GetFullPath($Path);$part;$part=[IO.Path]::GetDirectoryName($part)){
        if(Test-Path -LiteralPath $part){if(([IO.File]::GetAttributes($part)-band[IO.FileAttributes]::ReparsePoint)-ne 0){throw "Refusing reparse path: $part"}}
    }
}
function Assert-CleanTree([string]$Path){
    Assert-NoReparse $Path
    if(-not(Test-Path -LiteralPath $Path)){return}
    $stack=New-Object 'System.Collections.Generic.Stack[string]';$stack.Push($Path)
    while($stack.Count){foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($stack.Pop())){
        $attributes=[IO.File]::GetAttributes($entry)
        if(($attributes-band[IO.FileAttributes]::ReparsePoint)-ne 0){throw "Refusing reparse entry: $entry"}
        if(($attributes-band[IO.FileAttributes]::Directory)-ne 0){$stack.Push($entry)}
    }}
}
function Assert-PinnedFile([string]$Path,$Pin){
    if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){throw "Missing locked input: $Path"}
    Assert-NoReparse $Path
    if((Get-Item -LiteralPath $Path).Length-ne[long]$Pin.bytes){throw "Input integrity mismatch (length): $Path"}
    if(-not[String]::Equals((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash,[string]$Pin.sha256,[StringComparison]::OrdinalIgnoreCase)){throw "Input hash mismatch (SHA256): $Path"}
    if($Pin.PSObject.Properties['sha512']){
        $algorithm=[Security.Cryptography.SHA512]::Create();$stream=[IO.File]::OpenRead($Path)
        try{$actual=[Convert]::ToBase64String($algorithm.ComputeHash($stream))}finally{$stream.Dispose();$algorithm.Dispose()}
        if(-not[String]::Equals($actual,[string]$Pin.sha512,[StringComparison]::Ordinal)){throw "Input hash mismatch (SHA512): $Path"}
    }
}
function Get-NetworkBytes([string]$Url){
    $last=$null
    for($attempt=0;$attempt-lt 3;$attempt++){
        $client=New-Object Net.WebClient
        try{$client.Headers['User-Agent']='WinMemoryCleaner-Community-build';return ,($client.DownloadData($Url))}
        catch{
            $last=$_.Exception
            $gh=Get-Command gh -CommandType Application -ErrorAction SilentlyContinue
            if($gh-and($Url.StartsWith('https://api.github.com/',[StringComparison]::Ordinal)-or$Url.StartsWith('https://raw.githubusercontent.com/',[StringComparison]::Ordinal))){
                $process=New-Object Diagnostics.Process
                $process.StartInfo.FileName=$gh.Source;$process.StartInfo.Arguments='api "'+$Url+'"'
                $process.StartInfo.UseShellExecute=$false;$process.StartInfo.RedirectStandardOutput=$true;$process.StartInfo.RedirectStandardError=$true;$process.StartInfo.CreateNoWindow=$true
                $memory=New-Object IO.MemoryStream
                try{
                    if(-not$process.Start()){throw 'GitHub data fetch did not start.'}
                    $process.StandardOutput.BaseStream.CopyTo($memory);$errorText=$process.StandardError.ReadToEnd();$process.WaitForExit()
                    if($process.ExitCode-eq 0){return ,($memory.ToArray())}
                    $last=New-Object IO.IOException($errorText)
                }finally{$memory.Dispose();$process.Dispose()}
            }
        }finally{$client.Dispose()}
        Start-Sleep -Milliseconds (300*($attempt+1))
    }
    throw "Locked input download failed: $Url ($($last.Message))"
}
function Ensure-Input([string]$Name,$Pin,[string]$Url,[string[]]$Candidates,[switch]$GitBlob){
    foreach($candidate in $Candidates){if($candidate-and(Test-Path -LiteralPath $candidate -PathType Leaf)){Assert-PinnedFile $candidate $Pin;return $candidate}}
    $cached=Join-Path $OutputPath ('downloads\'+$Name)
    if(Test-Path -LiteralPath $cached -PathType Leaf){
        try{Assert-PinnedFile $cached $Pin;return $cached}catch{if($Offline){throw};Assert-NoReparse $cached;[IO.File]::Delete($cached)}
    }
    if($Offline){throw "Offline locked input is missing: $Name"}
    Assert-NoReparse $cached;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($cached))|Out-Null
    $data=Get-NetworkBytes $Url
    if($GitBlob){
        $blob=[Text.Encoding]::UTF8.GetString($data)|ConvertFrom-Json
        if($blob.encoding-ne 'base64'-or$blob.sha-ne $Pin.gitBlob-or[long]$blob.size-ne[long]$Pin.bytes){throw 'Git blob identity mismatch for official installer.'}
        $data=[Convert]::FromBase64String($blob.content)
        $header=[Text.Encoding]::ASCII.GetBytes('blob '+$data.Length+[char]0);$joined=New-Object byte[] ($header.Length+$data.Length)
        [Buffer]::BlockCopy($header,0,$joined,0,$header.Length);[Buffer]::BlockCopy($data,0,$joined,$header.Length,$data.Length)
        $sha=[Security.Cryptography.SHA1]::Create();try{$actual=([BitConverter]::ToString($sha.ComputeHash($joined))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
        if($actual-ne $Pin.gitBlob){throw 'Decoded installer Git blob hash mismatch.'}
    }
    $temporary=$cached+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    try{[IO.File]::WriteAllBytes($temporary,$data);Assert-PinnedFile $temporary $Pin;[IO.File]::Move($temporary,$cached)}
    finally{if(Test-Path -LiteralPath $temporary){[IO.File]::Delete($temporary)}}
    return $cached
}
function Expand-PinnedPackage([string]$Archive,[string]$Directory){
    Assert-CleanTree $Directory
    if(Test-Path -LiteralPath $Directory){[IO.Directory]::Delete($Directory,$true)}
    [IO.Directory]::CreateDirectory($Directory)|Out-Null
    $prefix=[IO.Path]::GetFullPath($Directory).TrimEnd('\')+'\';$zip=[IO.Compression.ZipFile]::OpenRead($Archive)
    try{
        if($zip.Entries.Count-gt 50000){throw 'Locked package contains too many entries.'}
        [long]$total=0
        foreach($entry in $zip.Entries){
            $total+=$entry.Length;if($total-gt 268435456){throw 'Locked package expands beyond 256MiB.'}
            $path=[IO.Path]::GetFullPath((Join-Path $Directory $entry.FullName))
            if(-not$path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Package entry escapes extraction root.'}
            if($entry.FullName.EndsWith('/')-or$entry.FullName.EndsWith('\')){[IO.Directory]::CreateDirectory($path)|Out-Null;continue}
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null
            $input=$entry.Open();$output=[IO.File]::Create($path)
            try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}
        }
    }finally{$zip.Dispose()}
}
Assert-NoReparse $OutputPath;[IO.Directory]::CreateDirectory($OutputPath)|Out-Null
foreach($package in $lock.packages){
    if($package.key-notmatch '^[a-z0-9.-]+$'-or$package.id-notmatch '^[a-z0-9.-]+$'-or$package.version-notmatch '^\d+\.\d+\.\d+$'){throw 'Unsafe package identity in lock.'}
    $name=$package.key+'.nupkg';$candidates=@()
    if($SourceCache){$candidates=@((Join-Path $SourceCache ('nupkg\'+$name)),(Join-Path $SourceCache $name))}
    $url='https://api.nuget.org/v3-flatcontainer/'+$package.id+'/'+$package.version+'/'+$package.id+'.'+$package.version+'.nupkg'
    $archive=Ensure-Input $name $package $url $candidates
    Expand-PinnedPackage $archive (Join-Path $OutputPath ('packages\'+$package.key))
}
$stage=Join-Path $OutputPath ('restore-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $stage 'lib'))|Out-Null;[IO.Directory]::CreateDirectory((Join-Path $stage 'inputs'))|Out-Null
try{
    foreach($asset in $lock.assets){
        if($asset.name-notmatch '^[A-Za-z0-9_.-]+\.dll$'){throw 'Unsafe runtime asset name.'}
        $source=Join-Path $OutputPath ('packages\'+$asset.package+'\'+$asset.path.Replace('/','\'));Assert-PinnedFile $source $asset
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage ('lib\'+$asset.name))
    }
    foreach($license in $lock.licenses){
        if($license.PSObject.Properties['package']){$source=Join-Path $OutputPath ('packages\'+$license.package+'\'+$license.path);Assert-PinnedFile $source $license}
        else{
            $candidates=@();if($SourceCache){$candidates=@((Join-Path $SourceCache $license.cache.Replace('/','\')))}
            $source=Ensure-Input $license.name $license $license.url $candidates
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage ('inputs\'+$license.name))
    }
    $candidates=@();if($SourceCache){$candidates=@((Join-Path $SourceCache 'downloads\PawnIO_setup.exe'),(Join-Path $SourceCache 'PawnIO_setup.exe'))}
    $installer=Ensure-Input $lock.installer.name $lock.installer $lock.installer.url $candidates -GitBlob
    $signature=Get-AuthenticodeSignature -LiteralPath $installer
    if($signature.Status-ne 'Valid'-or-not$signature.SignerCertificate-or-not[String]::Equals($signature.SignerCertificate.Thumbprint,$lock.installer.signerThumbprint,[StringComparison]::OrdinalIgnoreCase)){throw 'Official installer signature cannot be validated.'}
    $version=[Diagnostics.FileVersionInfo]::GetVersionInfo($installer)
    $actualVersion=New-Object Version($version.FileMajorPart,$version.FileMinorPart,$version.FileBuildPart,$version.FilePrivatePart)
    if(-not$actualVersion.Equals([Version]$lock.installer.version)){throw 'Official installer version mismatch.'}
    Copy-Item -LiteralPath $installer -Destination (Join-Path $stage 'inputs\PawnIO_setup.exe')
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'sensors\THIRD-PARTY-NOTICES.md') -Destination (Join-Path $stage 'inputs\THIRD-PARTY-NOTICES.md')
    foreach($name in @('lib','inputs')){
        $destination=Join-Path $OutputPath $name;Assert-CleanTree $destination
        if(Test-Path -LiteralPath $destination){[IO.Directory]::Delete($destination,$true)}
        [IO.Directory]::Move((Join-Path $stage $name),$destination)
    }
}finally{if(Test-Path -LiteralPath $stage){Assert-CleanTree $stage;[IO.Directory]::Delete($stage,$true)}}
[pscustomobject]@{packages=@($lock.packages).Count;runtimeDlls=@($lock.assets).Count;libraryDirectory=(Join-Path $OutputPath 'lib');inputsDirectory=(Join-Path $OutputPath 'inputs');referenceAssemblies=(Join-Path $OutputPath 'packages\microsoft.netframework.referenceassemblies.net472.1.0.3\build\.NETFramework\v4.7.2');installerExecuted=$false}|ConvertTo-Json
