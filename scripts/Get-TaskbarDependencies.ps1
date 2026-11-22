#requires -Version 7
$ErrorActionPreference = 'Stop'
$thirdParty = Join-Path (Split-Path $PSScriptRoot -Parent) 'third_party'
$destination = Join-Path $thirdParty 'translucenttb'
$manifest = Get-Content -LiteralPath (Join-Path $destination 'files.json') -Raw | ConvertFrom-Json

foreach ($file in $manifest | Where-Object { $_.PSObject.Properties['Url'] }) {
    $target = Join-Path $destination $file.Name
    if ((Test-Path -LiteralPath $target -PathType Leaf) -and (Get-FileHash -LiteralPath $target).Hash -eq $file.Sha256) {
        continue
    }
    # Stage on the same volume so concurrent or interrupted restores cannot publish partial files.
    $temporary = Join-Path $thirdParty ([IO.Path]::GetRandomFileName())
    $archivePath = "$temporary.zip"
    try {
        if ($file.PSObject.Properties['ArchiveEntry']) {
            Invoke-WebRequest -Uri $file.Url -OutFile $archivePath
            if ((Get-FileHash -LiteralPath $archivePath).Hash -ne $file.ArchiveSha256) { throw "Checksum mismatch: $($file.Url)" }
            $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                $entry = $zip.GetEntry($file.ArchiveEntry)
                if (!$entry) { throw "Missing archive entry: $($file.ArchiveEntry)" }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $temporary)
            } finally { $zip.Dispose() }
        } else {
            Invoke-WebRequest -Uri $file.Url -OutFile $temporary
        }
        if ((Get-FileHash -LiteralPath $temporary).Hash -ne $file.Sha256) { throw "Checksum mismatch: $($file.Name)" }
        [IO.File]::Move($temporary, $target, $true)
    } finally {
        [IO.File]::Delete($temporary)
        [IO.File]::Delete($archivePath)
    }
}
