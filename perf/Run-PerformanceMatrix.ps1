#Requires -Version 7.4
param(
    [ValidateRange(1,100)][int]$Repetitions=3,
    [ValidateRange(1,100000)][int]$Vus=500,
    [string]$Duration='10m',
    [ValidateSet('Participant','Fixed')][string]$Workload='Participant',
    [ValidateSet('None','Counters','Trace')][string]$DiagnosticsMode='Trace',
    [bool[]]$ObservabilityModes=@($true,$false),
    [string]$ApplicationSnapshot='',
    [switch]$ContinueOnFailure
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$matrixId='matrix-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$directory=Join-Path $PSScriptRoot "results/$matrixId"
$null=New-Item -ItemType Directory -Path $directory
$runs=[Collections.Generic.List[object]]::new()
for($iteration=1;$iteration -le $Repetitions;$iteration++){
    foreach($observability in $ObservabilityModes){
        $started=[DateTimeOffset]::UtcNow
        $label="round-$iteration-observability-$observability"
        $failure=$null
        try {
            & "$PSScriptRoot/Run-Performance.ps1" -Vus $Vus -Duration $Duration -Workload $Workload `
                -Observability $observability -DiagnosticsMode $DiagnosticsMode -ApplicationSnapshot $ApplicationSnapshot *>&1 |
                Tee-Object -FilePath (Join-Path $directory "$label.log")
        } catch { $failure=$_.Exception.Message; Write-Warning $failure }
        $report=@(Get-ChildItem (Join-Path $PSScriptRoot 'results') -Directory |
            Where-Object { $_.Name -match '^\d{14}-[a-z0-9]{6}$' -and $_.CreationTimeUtc -ge $started.UtcDateTime.AddSeconds(-1) } |
            Sort-Object CreationTimeUtc -Descending | Select-Object -First 1)
        $runs.Add(@{round=$iteration;observability=$observability;startedAt=$started.ToString('O');endedAt=[DateTimeOffset]::UtcNow.ToString('O');
            runId=if($report.Count){$report[0].Name}else{$null};passed=$null -eq $failure;failure=$failure})
        @{vus=$Vus;duration=$Duration;workload=$Workload;diagnosticsMode=$DiagnosticsMode;runs=$runs.ToArray()} |
            ConvertTo-Json -Depth 10 | Set-Content (Join-Path $directory 'matrix.json') -Encoding utf8
        if($failure -and !$ContinueOnFailure){throw "重复验收停止于失败轮次 $label。报告：$directory"}
    }
}
if(@($runs | Where-Object {!$_.passed}).Count){throw "重复验收存在失败，报告：$directory"}
& "$PSScriptRoot/Compare-Performance.ps1" -RunIds @($runs | ForEach-Object {$_.runId})
Write-Host "重复验收通过，报告：$directory"
