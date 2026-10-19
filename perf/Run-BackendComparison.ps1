#Requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$BaselineSnapshot,
    [ValidateRange(1,100)][int]$Repetitions=3,
    [ValidateRange(1,100000)][int]$Vus=50,
    [string]$Duration='90s'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$BaselineSnapshot=(Resolve-Path -LiteralPath $BaselineSnapshot).Path
if(!(Test-Path (Join-Path $BaselineSnapshot 'NoCTF.Host.dll'))){throw '基线目录缺少 NoCTF.Host.dll。'}
$directory=Join-Path $PSScriptRoot ('results/backend-comparison-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))
$null=New-Item -ItemType Directory -Path $directory
$runs=[Collections.Generic.List[object]]::new()
for($round=1;$round -le $Repetitions;$round++){
    foreach($variant in @('baseline','current')){
        $started=[DateTimeOffset]::UtcNow
        $snapshot=if($variant -eq 'baseline'){$BaselineSnapshot}else{''}
        $failure=$null
        try {
            & "$PSScriptRoot/Run-Performance.ps1" -Vus $Vus -Duration $Duration -Workload Fixed `
                -ApplicationSnapshot $snapshot -Observability $true -DiagnosticsMode Counters `
                -SubmissionIntervalSeconds 15 -PageDwellSeconds 20 -WarmupSeconds 20 *>&1 |
                Tee-Object -FilePath (Join-Path $directory "round-$round-$variant.log")
        } catch {$failure=$_.Exception.Message;Write-Warning $failure}
        $report=@(Get-ChildItem (Join-Path $PSScriptRoot 'results') -Directory |
            Where-Object {$_.Name -match '^\d{14}-[a-z0-9]{6}$' -and $_.CreationTimeUtc -ge $started.UtcDateTime.AddSeconds(-1)} |
            Sort-Object CreationTimeUtc -Descending | Select-Object -First 1)
        $runs.Add(@{variant=$variant;round=$round;runId=if($report.Count){$report[0].Name}else{$null};passed=$null -eq $failure;failure=$failure})
        @{vus=$Vus;duration=$Duration;workload='Fixed';applicationConnectionBudget=80;runs=$runs.ToArray()} |
            ConvertTo-Json -Depth 10 | Set-Content (Join-Path $directory 'runs.json') -Encoding utf8
    }
}
$comparable=@($runs | Where-Object {$_.runId -and (Test-Path (Join-Path $PSScriptRoot "results/$($_.runId)/diagnostics/inspection.json"))} | ForEach-Object {$_.runId})
if($comparable.Count){& "$PSScriptRoot/Compare-Performance.ps1" -RunIds $comparable}
if(@($runs | Where-Object {!$_.passed}).Count){throw "接口比较中存在失败轮次。报告：$directory"}
Write-Host "接口比较完成：$directory"
