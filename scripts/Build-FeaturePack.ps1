param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string]$OutputFile,[Parameter(Mandatory)][string]$ProtectedKey)
$ErrorActionPreference='Stop'
$directoryPath=(Resolve-Path -LiteralPath $Directory).Path
$header=Get-Content -LiteralPath (Join-Path $directoryPath 'expansion.json') -Raw | ConvertFrom-Json -AsHashtable
$files=@(Get-ChildItem -LiteralPath $directoryPath -File | Where-Object {$_.Name -notin @('expansion.json','publisher.sig')} | Sort-Object Name)
$header.files=[ordered]@{}
foreach($file in $files){$header.files[$file.Name]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
$bytes=[Text.Encoding]::UTF8.GetBytes(($header|ConvertTo-Json -Depth 32))
$rsa=[Security.Cryptography.RSA]::Create()
try {
    $private=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ProtectedKey)), $null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $read=0;$rsa.ImportPkcs8PrivateKey($private,[ref]$read)
    $signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
} finally {if($private){[Array]::Clear($private,0,$private.Length)};$rsa.Dispose()}
$destination=[IO.Path]::GetFullPath($OutputFile);[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null
$temp=$destination+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
try {
    $archive=[IO.Compression.ZipFile]::Open($temp,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$file.Name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
        foreach($item in @(@{name='expansion.json';data=$bytes},@{name='publisher.sig';data=$signature})){
            $stream=$archive.CreateEntry($item.name).Open()
            try {$stream.Write($item.data,0,$item.data.Length)} finally {$stream.Dispose()}
        }
    } finally {$archive.Dispose()}
    [IO.File]::Move($temp,$destination,$true)
} finally {if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
