$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try {
    $request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cleanupData)) | ConvertFrom-Json
    $cleanupRoot = [IO.Path]::GetFullPath($request.directory).TrimEnd('\')
    $temporaryRoot = [IO.Path]::GetFullPath($request.temporaryRoot).TrimEnd('\')
    if (![string]::Equals([IO.Path]::GetDirectoryName($cleanupRoot), $temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        ![IO.Path]::GetFileName($cleanupRoot).StartsWith('ZZZwallpaper-Uninstall-', [StringComparison]::Ordinal)) { exit 2 }
    function Assert-NoLinks([string]$path) {
        for ($node = $path; $node; $node = [IO.Path]::GetDirectoryName($node)) {
            if (([IO.File]::Exists($node) -or [IO.Directory]::Exists($node)) -and
                (([IO.File]::GetAttributes($node) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Linked path' }
        }
    }
    $marker = [IO.Path]::Combine($cleanupRoot, 'cleanup-owner.json')
    $executable = [IO.Path]::Combine($cleanupRoot, 'ZZZwallpaper.Uninstall.exe')
    function Read-Owner {
        Assert-NoLinks $marker
        $value = [IO.File]::ReadAllText($marker) | ConvertFrom-Json
        if ($value.AppId -ne 'D82EA01A-D414-4393-BE4D-7AD3371671C0' -or $value.Token -cne $request.Token -or
            $value.Sha256 -cne $request.Sha256 -or $value.InstallDirectory -cne $request.InstallDirectory -or
            $value.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid ownership marker' }
        return $value
    }
    $owner = Read-Owner
    if ($owner.ProcessId -gt 0) {
        $running = $null
        try { $running = [Diagnostics.Process]::GetProcessById($owner.ProcessId) } catch [ArgumentException] { }
        if ($running) {
            try {
                if ($running.StartTime.ToUniversalTime().Ticks -eq $owner.StartTicks) { $running.WaitForExit() }
            } finally { $running.Dispose() }
        }
    }
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            if (![IO.File]::Exists($marker)) { exit 0 }
            $current = Read-Owner
            if ($current.ProcessId -ne $owner.ProcessId -or $current.StartTicks -ne $owner.StartTicks) { exit 2 }
            Assert-NoLinks $executable
            if ([IO.File]::Exists($executable)) {
                $hashStream = [IO.File]::OpenRead($executable)
                $algorithm = [Security.Cryptography.SHA256]::Create()
                try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($hashStream)).Replace('-', '') }
                finally { $hashStream.Dispose(); $algorithm.Dispose() }
                if ($hash -cne $current.Sha256) { exit 2 }
                [IO.File]::Delete($executable)
            }
            if ($current.ProgramRemoved) {
                $installRoot = [IO.Path]::GetFullPath($current.InstallDirectory).TrimEnd('\')
                if ($installRoot -eq [IO.Path]::GetPathRoot($installRoot).TrimEnd('\')) { exit 2 }
                Assert-NoLinks $installRoot
                # Directory.Delete(false) also refuses a file added after enumeration.
                if ([IO.Directory]::Exists($installRoot) -and [IO.Directory]::GetFileSystemEntries($installRoot).Length -eq 0) {
                    [IO.Directory]::Delete($installRoot, $false)
                }
            }
            [IO.File]::Delete($marker)
            if ([IO.Directory]::GetFileSystemEntries($cleanupRoot).Length -eq 0) { [IO.Directory]::Delete($cleanupRoot, $false) }
            exit 0
        } catch {
            if ($attempt -eq 59) { [Console]::Error.WriteLine($_.Exception.Message) }
            [Threading.Thread]::Sleep(250)
        }
    }
    exit 1
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 2 }
