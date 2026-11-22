param([string]$ProjectDirectory=(Join-Path $PSScriptRoot '../src/Windows/ControlCenter'))
$ErrorActionPreference='Stop'
$ProjectDirectory=[IO.Path]::GetFullPath($ProjectDirectory)
$xamlNamespace='http://schemas.microsoft.com/winfx/2006/xaml'
$keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$resourceTypes=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$sources=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Read-Resources([string]$path) {
    $path=[IO.Path]::GetFullPath($path)
    if(!$sources.Add($path)) {return}
    [xml]$xml=[IO.File]::ReadAllText($path)
    foreach($node in $xml.SelectNodes('//*')) {
        $key=$node.GetAttribute('Key',$xamlNamespace)
        if($key) {[void]$keys.Add($key);$resourceTypes[$key]=$node.LocalName}
        if($node.LocalName -eq 'ResourceDictionary') {
            $source=$node.GetAttribute('Source')
            if($source) {Read-Resources (Join-Path (Split-Path $path -Parent) $source)}
        }
    }
}
Read-Resources (Join-Path $ProjectDirectory 'App.xaml')
# Geometry resources are prototypes, not live drawing objects. Passing one
# through a button property to Path.Data also creates a second owner (0.19.40).
# Use per-template inline geometry or a deep clone for each visual instead.
$contentChecks=0
$xamlFiles=@(Get-ChildItem -LiteralPath $ProjectDirectory -File -Filter '*.xaml')
$xamlFiles+=@(Get-ChildItem -LiteralPath (Join-Path $ProjectDirectory 'Themes') -File -Filter '*.xaml')
$xamlFiles+=@(Get-ChildItem -LiteralPath (Join-Path $ProjectDirectory 'Features') -Recurse -File -Filter '*.xaml')
foreach($file in $xamlFiles) {
    [xml]$xml=[IO.File]::ReadAllText($file.FullName)
    foreach($node in $xml.SelectNodes('//*')) {
        # WinUI accepts this markup at compile time but throws 0x800F1000
        # during layout if a native item presenter is nested in a panel.
        if($node.LocalName -in @('ListViewItemPresenter','GridViewItemPresenter') -and $node.ParentNode.LocalName -ne 'ControlTemplate') {
            throw "$($node.LocalName) must be the template root [$($file.Name)]. Use ContentPresenter inside composed item chrome."
        }
        $assignments=@()
        foreach($property in @('Content','Glyph','Data')) {$assignments+=@{Property=$property;Value=$node.GetAttribute($property)}}
        if($node.LocalName -eq 'Setter' -and $node.GetAttribute('Property') -match '(^|\.)(Content|Glyph|Data)$') {$assignments+=@{Property=$Matches[2];Value=$node.GetAttribute('Value')}}
        foreach($assignment in $assignments) {
            $value=$assignment.Value
            if($value -match '^\{(?:StaticResource|ThemeResource)\s+([^}]+)\}$') {
                $key=$Matches[1].Trim();$contentChecks++
                if($resourceTypes.ContainsKey($key) -and $resourceTypes[$key] -match 'Geometry$') {
                    throw "Shared Geometry resource '$key' cannot be assigned to $($assignment.Property) [$($file.Name)]. Create per-instance geometry or clone the prototype."
                }
            }
        }
    }
}
# Caption templates can be materialized before sibling application dictionaries
# are available. Require this dictionary to resolve every reference locally,
# in declaration order; finding a name somewhere in App.xaml is not enough.
$captionPath=Join-Path $ProjectDirectory 'Themes/ShellTheme.xaml'
[xml]$captionXml=[IO.File]::ReadAllText($captionPath)
$captionKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$captionReferences=0
foreach($resource in $captionXml.DocumentElement.ChildNodes) {
    if($resource.NodeType -ne [Xml.XmlNodeType]::Element) {continue}
    foreach($attribute in $resource.SelectNodes('.//@*')) {
        foreach($match in [regex]::Matches($attribute.Value,'\{(?:StaticResource|ThemeResource)\s+([^}]+)\}')) {
            $key=$match.Groups[1].Value.Trim();$captionReferences++
            if(!$captionKeys.Contains($key)) {throw "Caption resource '$key' is not defined before use in ShellTheme.xaml. Keep the caption dictionary self-contained."}
        }
    }
    $key=$resource.GetAttribute('Key',$xamlNamespace)
    if($key) {[void]$captionKeys.Add($key)}
}
# The silver theme has ThemeDictionaries, so WinUI loads its style eagerly.
# A resource in a sibling App dictionary is not in scope at that point. Check
# every local reference and every theme variant, not just the global key set.
$metalPath=Join-Path $ProjectDirectory 'Themes/MetalButtonTheme.xaml'
[xml]$metalXml=[IO.File]::ReadAllText($metalPath)
$metalKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$script:metalReferences=0
function Check-MetalReferences($node,$available) {
    foreach($attribute in $node.SelectNodes('.//@*')) {
        foreach($match in [regex]::Matches($attribute.Value,'\{(?:StaticResource|ThemeResource)\s+([^}]+)\}')) {
            $key=$match.Groups[1].Value.Trim();$script:metalReferences++
            if($key -match '^SystemColor(?:ButtonFace|ButtonText|Highlight)Color$') {continue}
            if(!$available.Contains($key)) {throw "Metal button resource '$key' is not defined before use in MetalButtonTheme.xaml. Sibling App dictionaries are unavailable during theme loading."}
        }
    }
}
foreach($resource in $metalXml.DocumentElement.ChildNodes) {
    if($resource.NodeType -ne [Xml.XmlNodeType]::Element) {continue}
    if($resource.LocalName -eq 'ResourceDictionary.ThemeDictionaries') {
        $common=$null
        foreach($theme in $resource.ChildNodes) {
            if($theme.NodeType -ne [Xml.XmlNodeType]::Element) {continue}
            $variant=[Collections.Generic.HashSet[string]]::new($metalKeys,[StringComparer]::Ordinal)
            foreach($entry in $theme.ChildNodes) {
                if($entry.NodeType -ne [Xml.XmlNodeType]::Element) {continue}
                Check-MetalReferences $entry $variant
                $key=$entry.GetAttribute('Key',$xamlNamespace)
                if($key) {[void]$variant.Add($key)}
            }
            if($null -eq $common) {$common=$variant} else {$common.IntersectWith($variant)}
        }
        if($null -ne $common) {$metalKeys.UnionWith($common)}
    } else {
        Check-MetalReferences $resource $metalKeys
        $key=$resource.GetAttribute('Key',$xamlNamespace)
        if($key) {[void]$metalKeys.Add($key)}
    }
}
$references=[Collections.Generic.List[object]]::new()
function Require-Resource([string]$key,[string]$source) {$references.Add([pscustomobject]@{Key=$key;Source=$source})}
$utilityPath=@(Get-ChildItem -LiteralPath $ProjectDirectory -Recurse -File -Filter 'MainWindow.UtilityPages.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }); if($utilityPath.Count -ne 1){throw 'Expected one utility page source'}; $utility=[IO.File]::ReadAllText($utilityPath[0].FullName)
$route=[regex]::Match($utility,'icon is (?<names>(?:"[^"]+"\s*(?:or\s*)?)+)')
if(!$route.Success) {throw 'UtilityIcon routing changed: update the resource validator.'}
$homeAliases=@([regex]::Matches($route.Groups['names'].Value,'"([^"]+)"') | ForEach-Object {$_.Groups[1].Value})
function Require-Utility([string]$icon,[string]$source) {
    $prefix=if($homeAliases -contains $icon) {'HomeIcon'} else {'UtilityIcon'}
    Require-Resource ($prefix+$icon) $source
}
foreach($icon in $homeAliases) {Require-Utility $icon 'UtilityIcon routing'}
foreach($file in Get-ChildItem -LiteralPath $ProjectDirectory -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $text=[IO.File]::ReadAllText($file.FullName)
    foreach($match in [regex]::Matches($text,'(?:HomeStyle|HomeBrush|CloneIconGeometry)\("([^"]+)"\)')) {Require-Resource $match.Groups[1].Value $file.Name}
    foreach($match in [regex]::Matches($text,'UtilityIcon\("([^"]+)"')) {Require-Utility $match.Groups[1].Value $file.Name}
    foreach($match in [regex]::Matches($text,'(?:HomeGeometry|CreateHomeVectorIcon)\("([^"]+)"')) {Require-Resource ('HomeIcon'+$match.Groups[1].Value) $file.Name}
    foreach($match in [regex]::Matches($text,'CreateEffectsVectorIcon\("([^"]+)"')) {Require-Resource ('EffectsIcon'+$match.Groups[1].Value) $file.Name}
    # Static choices passed through a loop are not checked by the XAML compiler.
    foreach($match in [regex]::Matches($text,'foreach\(string (?<variable>\w+) in new\[\]\{(?<names>[^}]+)\}\)[^;\r\n]*UtilityIcon\(\k<variable>,')) {
        foreach($name in [regex]::Matches($match.Groups['names'].Value,'"([^"]+)"')) {Require-Utility $name.Groups[1].Value $file.Name}
    }
    $widgetGlyph=[regex]::Match($text,'WidgetGlyph\([^;]+;')
    if($widgetGlyph.Success) {foreach($name in [regex]::Matches($widgetGlyph.Value,'"([^"]+)"')) {Require-Utility $name.Groups[1].Value $file.Name}}
}
foreach($file in Get-ChildItem -LiteralPath $ProjectDirectory -File -Filter '*.xaml') {
    $text=[IO.File]::ReadAllText($file.FullName)
    foreach($match in [regex]::Matches($text,'utility-(?:icon|info):([^"\s]+)')) {Require-Utility $match.Groups[1].Value $file.Name}
}
$missing=@($references | Where-Object {!$keys.Contains($_.Key)} | Sort-Object Key,Source -Unique)
if($missing.Count) {throw ('Missing application resources: '+(($missing | ForEach-Object {"$($_.Key) [$($_.Source)]"}) -join ', '))}
Write-Output "UI resource validation passed: $($references.Count) references in $($sources.Count) merged dictionaries; direct geometry sharing guard passed ($contentChecks resource assignments); $captionReferences caption and $metalReferences metal-button references resolve locally in declaration order."
