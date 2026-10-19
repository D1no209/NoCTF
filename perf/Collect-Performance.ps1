#Requires -Version 7.4
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$RunId,[ValidateRange(10,3600)][int]$Seconds=60)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/support/Environment.ps1"
. "$PSScriptRoot/support/Diagnostics.ps1"
$original=Get-Content "$PSScriptRoot/.local/$RunId/state.json" -Raw | ConvertFrom-Json -AsHashtable
$process=Get-Process -Id $original.hostPid -ErrorAction Stop
if($process.StartTime.ToUniversalTime().Ticks -ne ([DateTimeOffset]$original.hostStartTime).UtcTicks){throw 'Host 进程身份发生变化。'}
$capture='capture-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6)
$local=Join-Path $original.local $capture
$reports=Join-Path $original.reports $capture
$null=New-Item -ItemType Directory -Path $local,(Join-Path $reports 'diagnostics') -Force
$state=$original.Clone();$state.local=$local;$state.reports=$reports
$config=Get-Content "$PSScriptRoot/.local/$RunId/k6.json" -Raw | ConvertFrom-Json -AsHashtable
$config.diagnosticsMode='Trace';$config.traceDelaySeconds=0;$config.traceSeconds=[Math]::Min(30,$Seconds-5)
# 独立采集窗口保留原始负载参数，窗口计时从本次采集开始。
$config.durationSeconds=$Seconds;$config.originalWorkloadRunId=$RunId
Initialize-PerfDiagnostics $PSScriptRoot $reports $config
$symbols=Join-Path $reports 'diagnostics/symbols'
$null=New-Item -ItemType Directory -Path $symbols -Force
Get-ChildItem $state.binaryDirectory -File | Where-Object {$_.Name -like 'NoCTF.*' -and $_.Extension -in @('.pdb','.dll')} | Copy-Item -Destination $symbols
Save-PerfJson $state (Join-Path $local 'state.json')
Save-PerfJson $config (Join-Path $local 'config.json')
Save-PerfJson @{utc=[DateTimeOffset]::UtcNow.ToString('O')} (Join-Path $local 'measurement.json')
& "$PSScriptRoot/support/Collect-Diagnostics.ps1" -StateFile (Join-Path $local 'state.json') -ConfigFile (Join-Path $local 'config.json') -CollectionSeconds $Seconds
Write-Host "独立采集目录：$reports"
