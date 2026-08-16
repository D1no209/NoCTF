param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"
$tar = Get-Command tar.exe -ErrorAction Stop
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

foreach ($kit in @("awd", "awdp")) {
    $archive = Join-Path $resolvedOutput "noctf-$kit-authoring-kit.tar.gz"
    & $tar.Source --exclude "$kit/artifacts" -czf $archive -C $PSScriptRoot $kit
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create $archive"
    }
    Write-Output $archive
}
