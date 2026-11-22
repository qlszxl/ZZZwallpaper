[CmdletBinding()]
param([string]$SourceDirectory,[string]$CompilerPath,[switch]$CheckOnly,[switch]$PreviewOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PreviewOnly) {
    if($SourceDirectory -or $CompilerPath -or $CheckOnly) { throw 'Use -PreviewOnly by itself.' }
    & (Join-Path $PSScriptRoot 'Build-InstallerPreview.ps1')
    return
}
$parameters=@{CheckOnly=$CheckOnly}
if($SourceDirectory) { $parameters.SourceDirectory=$SourceDirectory }
if($CompilerPath) { $parameters.CompilerPath=$CompilerPath }
& (Join-Path $PSScriptRoot 'Build-TestInstaller.ps1') @parameters