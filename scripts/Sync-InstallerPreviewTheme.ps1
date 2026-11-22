Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$themeRoot = Join-Path $root 'src/Windows/ControlCenter/Themes'
$destination = Join-Path $root 'src/Windows/InstallerPreview/Themes/SoftwareTokens.g.xaml'
$presentationNs = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
$xamlNs = 'http://schemas.microsoft.com/winfx/2006/xaml'
$result = [xml]('<ResourceDictionary xmlns="' + $presentationNs + '" xmlns:x="' + $xamlNs + '" />')
$resources = [ordered]@{}
# Export the shared palette's initial color as a WPF-compatible static brush.
# The installer keeps its own existing palette lifecycle; WinUI interaction
# bindings and the application's animation coordinator never cross frameworks.
$palettePath = Join-Path $root 'src/Shared/Contracts/AppearanceOptions.cs'
$paletteText = [IO.File]::ReadAllText($palettePath)
$palette = [regex]::Match($paletteText, 'new\(\s*"lime"\s*,\s*"[^"]*"\s*,\s*"(?<accent>#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)"\s*,\s*"(?<accentEnd>#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)"')
if (!$palette.Success) { throw 'Shared lime SkinPalette could not be read. Update the installer theme palette extraction.' }
$interactionColor = $palette.Groups['accent'].Value
function New-InteractionFallback([string]$key) {
    $brush = $result.CreateElement('SolidColorBrush', $presentationNs)
    $keyAttribute = $result.CreateAttribute('x', 'Key', $xamlNs)
    $keyAttribute.Value = $key
    [void]$brush.Attributes.Append($keyAttribute)
    $brush.SetAttribute('Color', $interactionColor)
    return $brush
}
foreach ($name in @('InteractionTheme.xaml','ZZZTheme.xaml','HomeSurfaces.xaml','HomeTheme.xaml','ShellTheme.xaml')) {
    [xml]$source = Get-Content -LiteralPath (Join-Path $themeRoot $name) -Raw
    foreach ($node in $source.DocumentElement.ChildNodes) {
        if ($node.LocalName -in @('SolidColorBrush','LinearGradientBrush','CompositionAccentBrush')) {
            $key = $node.GetAttribute('Key', $xamlNs)
            if ($key) {
                if ($node.LocalName -eq 'CompositionAccentBrush' -or $key -eq 'InteractionAccentBrush' -or $node.GetAttribute('Color') -in @('{Binding Color, Source={StaticResource InteractionAccentBrush}}','{Binding FallbackColor, Source={StaticResource InteractionAccentBrush}}')) {
                    $resources[$key] = New-InteractionFallback $key
                } else { $resources[$key] = $node }
            }
        }
    }
}
# WPF templates retain these public names. Keep compatibility aliases in the
# export only, so application controls all reference InteractionAccentBrush.
foreach ($key in @('HomeSelectionBrush','HomeControlAccent','HomeControlAccentHover','HomeControlAccentPressed',
    'EffectsAccent','Accent','HomeComboSelectionBackground','HomeComboSelectionHoverBackground',
    'HomeScreenSelectionBackground','HomeScreenSelectionHoverBackground','ShellCaptionAccent','ShellCaptionAccentPressed')) {
    $resources[$key] = New-InteractionFallback $key
}
if (!$resources.Contains('HomeControlInk')) { throw 'HomeControlInk is required by the installer caption fallback.' }
$captionInk = $resources['HomeControlInk'].CloneNode($true)
[void]$captionInk.SetAttribute('Key', $xamlNs, 'ShellCaptionInk')
$resources['ShellCaptionInk'] = $captionInk
foreach ($node in $resources.Values) { [void]$result.DocumentElement.AppendChild($result.ImportNode($node, $true)) }
[xml]$icons = Get-Content -LiteralPath (Join-Path $themeRoot 'HomeIcons.xaml') -Raw
foreach ($node in $icons.DocumentElement.ChildNodes) {
    if ($node.LocalName -eq 'PathGeometry' -and $node.GetAttribute('Key', $xamlNs) -in @(
        'HomeIconMonitor','HomeIconPlay','HomeIconPause','HomeIconChevronRight','HomeIconChevronDown','HomeIconClose','HomeIconCheck')) {
        [void]$result.DocumentElement.AppendChild($result.ImportNode($node, $true))
    }
}
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create($destination, $settings)
try { $result.Save($writer) } finally { $writer.Dispose() }
Write-Output 'Native preview theme brushes and vector icons synchronized from ControlCenter.'
