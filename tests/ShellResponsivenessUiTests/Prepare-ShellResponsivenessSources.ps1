[CmdletBinding()]
param(
    [string]$ModuleDirectory = (Join-Path $PSScriptRoot '../..'),
    [string]$BaselineDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$ModuleDirectory = [IO.Path]::GetFullPath($ModuleDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($BaselineDirectory) { $BaselineDirectory = [IO.Path]::GetFullPath($BaselineDirectory) }
if ([IO.Directory]::Exists($OutputDirectory) -and [IO.Directory]::EnumerateFileSystemEntries($OutputDirectory).GetEnumerator().MoveNext()) {
    throw 'Generated source directory must be new or empty.'
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$records = @()

function Replace-One([string]$Text, [string]$Pattern, [string]$Replacement, [string]$Description) {
    $regex = [regex]::new($Pattern)
    if ($regex.Matches($Text).Count -ne 1) { throw "Expected one source location: $Description" }
    return $regex.Replace($Text, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $Replacement }, 1)
}

function Measure-Method([string]$Text, [string]$Name, [string]$Counter) {
    $pattern = '(?m)^([ \t]*(?:private|internal)\s+(?:static\s+)?[^\r\n;{}=]+\b' + [regex]::Escape($Name) + '\([^\r\n;{}]*\)\s*\r?\n[ \t]*\{)'
    $regex = [regex]::new($pattern)
    if ($regex.Matches($Text).Count -ne 1) { throw "Expected one method body: $Name" }
    return $regex.Replace($Text, [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $match.Value + "`r`n        using var shellPerfScope = ShellPerfInstrumentation.Measure(`"$Counter`");"
    }, 1)
}

foreach ($name in @('MainWindow.Placement.cs', 'MainWindow.Chrome.cs', 'MainWindow.Branding.cs')) {
    $path = if ($BaselineDirectory) { Join-Path $BaselineDirectory $name } else { Join-Path $ModuleDirectory "src/Windows/ControlCenter/Shell/$name" }
    $text = [IO.File]::ReadAllText($path)
    $patches = @()
    if ($name -eq 'MainWindow.Placement.cs') {
        # Only construction's first restore is skipped. The actual production
        # Capture/Changed/timer/Save/Shutdown implementation is preserved.
        $text = Replace-One $text '(?m)^[ \t]*RestoreWindowPlacement\(\);[ \t]*\r?$' '        // Isolated fixture: skip only initial visible placement restoration.' 'initial RestoreWindowPlacement statement'
        $patches += 'Skip initial RestoreWindowPlacement only'
        foreach ($method in @('MonitorIdentity', 'CaptureWindowPlacement', 'SaveWindowPlacement')) {
            $text = Measure-Method $text $method $method
            $patches += "Measure $method"
        }
        $text = Replace-One $text 'AppWindow\.Changed\s*\+=\s*\(_,args\)\s*=>\s*\{' "AppWindow.Changed+=(_,args)=> {`r`n            using var shellChangedScope=ShellPerfInstrumentation.Measure(`"PlacementChanged`");" 'production placement Changed handler'
        $patches += 'Measure production placement Changed handler'
        $nativePattern = '\[DllImport\("user32\.dll",\s*CharSet\s*=\s*CharSet\.Unicode\)\]\s*private static extern bool EnumDisplayDevices\(string device,\s*uint index,\s*ref MonitorDisplayDevice info,\s*uint flags\);'
        $nativeReplacement = @'
[DllImport("user32.dll",EntryPoint="EnumDisplayDevicesW",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern bool ShellPerfNativeEnumDisplayDevices(string device,uint index,ref MonitorDisplayDevice info,uint flags);
    private static bool EnumDisplayDevices(string device,uint index,ref MonitorDisplayDevice info,uint flags)
    {
        using var shellPerfScope=ShellPerfInstrumentation.Measure("EnumDisplayDevices");
        return ShellPerfNativeEnumDisplayDevices(device,index,ref info,flags);
    }
'@
        $text = Replace-One $text $nativePattern $nativeReplacement 'native display enumeration wrapper'
        $text = Replace-One $text 'modules\.Settings\.Set\("window\.placement\.v1",\s*json\);' 'ShellPerfInstrumentation.Count("placementWrites"); modules.Settings.Set("window.placement.v1",json);' 'placement storage write'
        $patches += 'Measure native EnumDisplayDevices call', 'Count actual placement storage writes'
    }
    if ($name -eq 'MainWindow.Chrome.cs') {
        foreach ($method in @('ShellWindow_Changed', 'UpdateShellInputRegions', 'UpdateShellCaptionState')) {
            $text = Measure-Method $text $method $method
            $patches += "Measure $method"
        }
        $regionPattern = 'shellNonClientInput\.SetRegionRects\(NonClientRegionKind\.Passthrough,\s*([^\r\n;]+)\);'
        $regionRegex = [regex]::new($regionPattern)
        if ($regionRegex.Matches($text).Count -ne 1) { throw 'Expected one input region submission.' }
        $text = $regionRegex.Replace($text, [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            'shellNonClientInput.SetRegionRects(NonClientRegionKind.Passthrough, ShellPerfInstrumentation.ObserveInputRegions(' + $match.Groups[1].Value + '));'
        }, 1)
        $patches += 'Count actual input region submission'
    }
    if ($name -eq 'MainWindow.Branding.cs') {
        foreach ($method in @('UpdateShellWordmark', 'ShellBranding_LayoutUpdated')) {
            $text = Measure-Method $text $method $method
            $patches += "Measure $method"
        }
        $text = Replace-One $text 'shellWindowRegion\.Update\(' 'ShellPerfInstrumentation.Count("wordmarkRegionUpdate"); shellWindowRegion.Update(' 'wordmark region update'
        $text = Replace-One $text 'ShellHeaderRow\.Height\s*=' 'ShellPerfInstrumentation.Count("wordmarkLayoutWrites"); ShellHeaderRow.Height =' 'wordmark layout property write'
        $patches += 'Count wordmark region update calls', 'Count header layout writes'
    }
    $destination = Join-Path $OutputDirectory $name
    [IO.File]::WriteAllText($destination, $text, [Text.UTF8Encoding]::new($false))
    $records += [ordered]@{
        source = $path
        sourceSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        generated = $destination
        generatedSha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        patches = $patches
    }
}
$manifest = [ordered]@{
    mode = $(if ($BaselineDirectory) { 'baseline' } else { 'optimized' })
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    initialRestoreSkipped = $true
    productionPlacementAndChromeHandlersPreserved = $true
    files = $records
    dependencies = @(
        if (!$BaselineDirectory) {
            $dependencyPath = Join-Path $ModuleDirectory 'src/Windows/ControlCenter/Shell/WindowPlacementGeometry.cs'
            [ordered]@{
                source = $dependencyPath
                sourceSha256 = (Get-FileHash -LiteralPath $dependencyPath -Algorithm SHA256).Hash.ToLowerInvariant()
                compiledFromProductionSource = $true
            }
        }
    )
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'instrumentation-manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$manifest
