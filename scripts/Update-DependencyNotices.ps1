[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packages = Join-Path $root '.nuget/packages'
$locks = @('src/Windows/ControlCenter/packages.lock.json','src/Windows/WidgetHost/packages.lock.json',
    'src/Windows/McpBridge/packages.lock.json','src/Windows/InstallerPreview/packages.lock.json',
    'src/Shared/Storage/packages.lock.json')
$entries = @{}
foreach ($relative in $locks) {
    $locked = Get-Content -LiteralPath (Join-Path $root $relative) -Raw | ConvertFrom-Json
    foreach ($framework in $locked.dependencies.PSObject.Properties) {
        foreach ($dependency in $framework.Value.PSObject.Properties) {
            if ($dependency.Value.type -eq 'Project') { continue }
            $id = $dependency.Name; $version = $dependency.Value.resolved
            $key = $id.ToLowerInvariant() + '/' + $version
            if (!$entries.ContainsKey($key)) { $entries[$key] = @{ Id=$id; Version=$version; Locks=[Collections.Generic.List[string]]::new() } }
            if (!$entries[$key].Locks.Contains($relative)) { $entries[$key].Locks.Add($relative) }
        }
    }
}
$text = [Text.StringBuilder]::new()
[void]$text.AppendLine('ZZZwallpaper - locked NuGet dependency notices')
[void]$text.AppendLine('Generated from packages.lock.json and restored package metadata. Includes build-only and platform-specific packages; not every listed package is redistributed. Package licenses remain independent of the application license.')
[void]$text.AppendLine('For packages declaring a license expression without embedding the text, see the matching full license files and THIRD-PARTY-NOTICES.md. This inventory does not replace corresponding-source obligations.')
$missing = [Collections.Generic.List[string]]::new()
$includedTexts = @{}
foreach ($key in ($entries.Keys | Sort-Object)) {
    $entry = $entries[$key]; $directory = Join-Path $packages $key
    $nuspec = Join-Path $directory ($entry.Id.ToLowerInvariant()+'.nuspec')
    if (!(Test-Path -LiteralPath $nuspec -PathType Leaf)) { throw "Missing locked package metadata: $key" }
    [xml]$xml = Get-Content -LiteralPath $nuspec -Raw
    $metadata = $xml.DocumentElement.SelectSingleNode('*[local-name()="metadata"]')
    [void]$text.AppendLine("`n--- $($entry.Id) $($entry.Version) ---")
    [void]$text.AppendLine('Lock files: '+($entry.Locks -join ', '))
    foreach ($name in @('authors','copyright','license','licenseUrl','projectUrl')) {
        $node = $metadata.SelectSingleNode('*[local-name()="'+$name+'"]')
        if ($node -and $node.InnerText) { [void]$text.AppendLine($name+': '+$node.InnerText) }
    }
    $repository = $metadata.SelectSingleNode('*[local-name()="repository"]')
    if ($repository) { [void]$text.AppendLine('repository: '+$repository.GetAttribute('url')+' '+$repository.GetAttribute('commit')) }
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    $files = [Collections.Generic.List[string]]::new()
    if ($license -and $license.GetAttribute('type') -eq 'file') {
        $source = [IO.Path]::GetFullPath((Join-Path $directory $license.InnerText))
        if (!$source.StartsWith([IO.Path]::GetFullPath($directory)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid license path' }
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing package license: $key" }
        $files.Add($source)
    }
    foreach ($file in (Get-ChildItem -LiteralPath $directory -File | Where-Object Name -Match '^(LICENSE|NOTICE|THIRD-PARTY-NOTICES)(\.[^.]+)?$' | Sort-Object Name)) {
        if (!$files.Contains($file.FullName)) { $files.Add($file.FullName) }
    }
    foreach ($file in $files) {
        [void]$text.AppendLine('--- Original package text: '+[IO.Path]::GetFileName($file)+' ---')
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if ($includedTexts.ContainsKey($hash)) {
            [void]$text.AppendLine('Identical original text already included under: '+$includedTexts[$hash])
        } else {
            $includedTexts[$hash] = "$($entry.Id) $($entry.Version) / $([IO.Path]::GetFileName($file))"
            [void]$text.AppendLine([IO.File]::ReadAllText($file))
        }
    }
    if (!$license -and $files.Count -eq 0) { $missing.Add($key) }
}
[void]$text.AppendLine("`nPackages without embedded license text or an expression (review their licenseUrl): "+($missing -join ', '))
$destination = Join-Path $root 'third_party/licenses/NuGet-NOTICES.txt'
[IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
[IO.File]::WriteAllText($destination,$text.ToString(),[Text.UTF8Encoding]::new($false))
Write-Output "Generated notices for $($entries.Count) locked packages; legacy metadata entries: $($missing.Count)."
