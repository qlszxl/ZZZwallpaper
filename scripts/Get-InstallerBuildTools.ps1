[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$packageDirectory=Join-Path $root 'third_party/build-packages'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

function Get-PinnedBuildPackage([string]$Url,[string]$Destination,[string]$Sha256) {
    if(!(Test-Path -LiteralPath $Destination -PathType Leaf)) {
        Invoke-WebRequest -Uri $Url -OutFile $Destination
    }
    if((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Build package checksum mismatch: $Destination"
    }
}

$compilerPackage=Join-Path $root 'third_party/inno-setup-6.7.3.nupkg'
Get-PinnedBuildPackage 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.3/tools.innosetup.6.7.3.nupkg' $compilerPackage 'F780898E402FF80612CC8D9FCB8C6E02932BD1CB4C900FFDAA31F9341CFB49F4'
$compilerDirectory=Join-Path $root 'third_party/inno-setup-6.7.3'
if(!(Test-Path -LiteralPath (Join-Path $compilerDirectory 'tools/ISCC.exe') -PathType Leaf)) {
    # Extract build tooling into the workspace; never run its installation program.
    [IO.Compression.ZipFile]::ExtractToDirectory($compilerPackage,$compilerDirectory,$true)
}
foreach($name in @('ISCC.exe','ISCmplr.dll','ISPP.dll')) {
    $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $compilerDirectory "tools/$name")
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B.V.') {
        throw "Unexpected compiler signature: $name"
    }
}
Get-PinnedBuildPackage 'https://api.nuget.org/v3-flatcontainer/microsoft.net.illink.tasks/8.0.8/microsoft.net.illink.tasks.8.0.8.nupkg' (Join-Path $packageDirectory 'microsoft.net.illink.tasks.8.0.8.nupkg') 'B2DED474FC7D00529FF68E967B0B2650A85B3F6206D8A4231D109374FE158F4D'
Write-Output 'Pinned installer build tools are ready in the workspace. No software was installed.'
