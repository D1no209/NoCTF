param([Parameter(Mandatory)][string]$RunId, [switch]$RemoveData)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/support/Environment.ps1"
. "$PSScriptRoot/support/Diagnostics.ps1"
if ($RunId -notmatch '^[a-z0-9-]+$') { throw 'RunId 格式无效。' }
$state = Get-Content -LiteralPath "$PSScriptRoot/.local/$RunId/state.json" -Raw | ConvertFrom-Json -AsHashtable
if($state.runId -ne $RunId -or $state.project -ne "noctf-perf-$RunId"){throw '环境记录与指定运行标识不一致。'}
$diagnosticsFailure=$null
$hostFailure=$null
try { Stop-PerfDiagnostics $state } catch { $diagnosticsFailure=$_ }
try {
if ($state.ContainsKey('hostPid')) {
    $process = Get-Process -Id $state.hostPid -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        $recordedStart = [DateTimeOffset]$state.hostStartTime
        if ($process.StartTime.ToUniversalTime().Ticks -ne $recordedStart.UtcTicks -or $process.ProcessName -ne 'dotnet') { throw '进程身份已经改变，停止操作已终止。' }
        Stop-Process -Id $process.Id
        $process.WaitForExit(30000) | Out-Null
    }
}
} catch { $hostFailure=$_ }
$arguments = @('down')
if ($RemoveData) { $arguments += '--volumes' }
Invoke-PerfCompose $state $arguments
Save-PerfJson @{runId=$RunId;project=$state.project;composeStopped=$true;hostStopped=$null -eq $hostFailure;volumesRemoved=[bool]$RemoveData;utc=[DateTimeOffset]::UtcNow.ToString('O')} (Join-Path $state.reports 'cleanup.json')
Write-Host "测试环境已停止：$RunId。报告保留在 perf/results/$RunId。"
if($null -ne $hostFailure){throw $hostFailure}
if($null -ne $diagnosticsFailure){throw $diagnosticsFailure}
