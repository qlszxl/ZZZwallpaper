$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$checks=0
function Require([bool]$condition,[string]$message){if(!$condition){throw $message};$script:checks++}
foreach($module in @('Application','Storage')){
    $directory=Join-Path $root "src/Shared/$module"
    [xml]$project=Get-Content -LiteralPath (Join-Path $directory "$module.csproj") -Raw
    Require ($project.Project.PropertyGroup.TargetFramework -eq 'net8.0') "$module must be UI independent."
    foreach($reference in $project.SelectNodes('//ProjectReference')){
        Require ($reference.Include -eq '../Contracts/Contracts.csproj') "$module has an unexpected project dependency: $($reference.Include)"
    }
    $sources=Get-ChildItem -LiteralPath $directory -Recurse -File -Filter '*.cs' | Where-Object {$_.FullName -notmatch '\\(bin|obj)\\'}
    foreach($source in $sources){
        $text=[IO.File]::ReadAllText($source.FullName)
        Require ($text -notmatch 'Microsoft\.UI|System\.Windows\.Forms|LumaWall\.ControlCenter|LumaWall\.Automation') "$module depends on UI/transport: $($source.Name)"
        if($module -eq 'Application'){Require ($text -notmatch 'Microsoft\.Data\.Sqlite|SqliteConnection|LumaWall\.Storage') "Application depends on a storage implementation: $($source.Name)"}
    }
}
$cc=Join-Path $root 'src/Windows/ControlCenter'
Require (@(Get-ChildItem -LiteralPath $cc -Recurse -File -Filter 'LibraryStore*.cs').Count -eq 0) 'Storage implementation leaked into ControlCenter.'
foreach($name in @('EffectEditSession','SharingPresenter','SharingActivationCoordinator','LibraryImportCoordinator','DesktopRuntimePump')){
    Require (@(Get-ChildItem -LiteralPath $cc -Recurse -File -Filter "$name.cs").Count -eq 1) "Missing or duplicated module: $name"
}
$effects=[IO.File]::ReadAllText((Join-Path $root 'src/Shared/Contracts/VideoEffects.cs'))
Require ($effects.Contains('=40;')) 'Effects configuration must use schema 40.'
Require ($effects.Contains('Invariant($"25 ')) 'Legacy native wire compatibility changed.'
Require ($effects.Contains('(package.Length==0?"37":"38")+legacy[2..]')) 'Built-in effects keep wire 37 and packaged shaders use wire 38.'
foreach($name in @('StorageTests','AutomationTests','MaterialTests')){
    $project=[IO.File]::ReadAllText((Join-Path $root "tests/$name/$name.csproj"))
    Require ($project -notmatch '<Compile[^>]+LibraryStore') "$name must reference the real Storage assembly."
}
Write-Output "Module boundaries verified: $checks checks passed."


