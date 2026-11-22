param([Parameter(Mandatory)][string]$ProtectedKey,[Parameter(Mandatory)][string]$PublicKey)
$ErrorActionPreference='Stop'
if ((Test-Path -LiteralPath $ProtectedKey) -or (Test-Path -LiteralPath $PublicKey)) { throw 'Use new paths. Existing publisher keys are never replaced.' }
$rsa=[Security.Cryptography.RSA]::Create(3072)
try {
    $private=$rsa.ExportPkcs8PrivateKey()
    $protected=[Security.Cryptography.ProtectedData]::Protect($private,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes($ProtectedKey,$protected)
    [IO.File]::WriteAllText($PublicKey,$rsa.ExportSubjectPublicKeyInfoPem())
} finally { if($private){[Array]::Clear($private,0,$private.Length)};$rsa.Dispose() }
