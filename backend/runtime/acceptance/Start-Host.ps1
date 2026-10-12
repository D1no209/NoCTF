[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$EnvironmentFile
)
$ErrorActionPreference = 'Stop'
$configuration = Get-Content -LiteralPath $EnvironmentFile -Raw | ConvertFrom-Json -AsHashtable
if ($configuration.synthetic -ne $true) { throw 'A synthetic local acceptance configuration is required.' }
foreach ($entry in $configuration.environment.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, 'Process')
}
$executable = Join-Path ([IO.Path]::GetFullPath($Directory)) 'NoCTF.Host.exe'
& $executable --runtime-info
if ($LASTEXITCODE -ne 0) { throw 'Runtime identity verification failed.' }
Push-Location $Directory
try { & $executable; exit $LASTEXITCODE } finally { Pop-Location }
