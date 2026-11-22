# A git archive carries the release commit even when extracted inside another repository.
$root = Split-Path $PSScriptRoot -Parent
$stamp = (Get-Content -LiteralPath (Join-Path $root '.git-revision') -Raw).Trim()
if ($stamp -match '^[0-9a-f]{40}$') { return $stamp }
if (Get-Command git -ErrorAction SilentlyContinue) {
    git -C $root ls-files --error-unmatch -- .git-revision 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        git -C $root diff --quiet HEAD -- .
        if ($LASTEXITCODE -ne 0) { return }
        $revision = git -C $root rev-parse --verify HEAD
        if ($LASTEXITCODE -eq 0 -and $revision -match '^[0-9a-f]{40}$') { return $revision }
    }
}
