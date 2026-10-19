#Requires -Version 7.4
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string[]]$RunIds)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/support/Environment.ps1"
$directory=Join-Path $PSScriptRoot ('results/comparison-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))
$null=New-Item -ItemType Directory -Path $directory
$runs=@(foreach($id in $RunIds){
    $root=Join-Path $PSScriptRoot "results/$id"
    $config=Get-Content (Join-Path $root 'configuration.json') -Raw | ConvertFrom-Json -AsHashtable
    $inspection=Get-Content (Join-Path $root 'diagnostics/inspection.json') -Raw | ConvertFrom-Json -AsHashtable
    $verification=Get-Content (Join-Path $root 'verification.json') -Raw | ConvertFrom-Json -AsHashtable
    $cleanup=Get-Content (Join-Path $root 'cleanup.json') -Raw | ConvertFrom-Json -AsHashtable
    $summary=Get-Content (Join-Path $root 'summary.json') -Raw | ConvertFrom-Json -AsHashtable
    $application=if(Test-Path (Join-Path $root 'application.json')){Get-Content (Join-Path $root 'application.json') -Raw | ConvertFrom-Json -AsHashtable}else{$null}
    $fingerprint=if($null -ne $application){
        $manifest=(@($application.assemblies | Sort-Object name | ForEach-Object {"$($_.name):$($_.sha256)"}) -join "`n")
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifest)))
    }else{'unrecorded-'+$id}
    $workloadManifest=(@($config.scriptHashes | Sort-Object name | ForEach-Object {"$($_.name):$($_.sha256)"}) -join "`n")
    $workloadFingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($workloadManifest)))
    $failedPerformance=@($inspection.failedThresholds | Where-Object {$_ -match '^(http_req_duration|http_req_failed|evaluation_duration|hub_call_duration)'})
    $businessThresholds=@($inspection.failedThresholds | Where-Object {$_ -notmatch '^(http_req_duration|http_req_failed|evaluation_duration|hub_call_duration)'})
    @{runId=$id;vus=$config.vus;duration=$config.duration;observability=$config.observability;workload=$config.workload;
        diagnosticsMode=$config.diagnosticsMode;submissionIntervalSeconds=$config.submissionIntervalSeconds;
        captureDatabasePlanning=if($config.ContainsKey('captureDatabasePlanning')){$config.captureDatabasePlanning}else{$null};
        scriptHashes=$config.scriptHashes;application=$application;applicationFingerprint=$fingerprint;workloadFingerprint=$workloadFingerprint;
        performancePassed=$failedPerformance.Count -eq 0;businessPassed=$businessThresholds.Count -eq 0 -and $verification.passed;
        failedPerformance=$failedPerformance;failedBusiness=$businessThresholds;verification=$verification;
        cleanupPassed=$cleanup.hostStopped -and $cleanup.composeStopped;diagnosticsComplete=$inspection.collectionComplete;
        failedSessions=$inspection.failedSessions;requestsPerSecond=$inspection.measurementRequestsPerSecond;
        failureRate=$inspection.httpFailureRate;operations=$inspection.requests;sessions=if($inspection.ContainsKey('sessions')){$inspection.sessions}else{$null};
        evaluation=$summary.metrics.evaluation_duration.values}
})
$aggregates=@(foreach($group in ($runs | Group-Object {"$($_.vus)|$($_.duration)|$($_.workload)|$($_.observability)|$($_.diagnosticsMode)|$($_.applicationFingerprint.Substring(0,12))|$($_.workloadFingerprint.Substring(0,12))"})){
    foreach($endpoint in @($group.Group.operations.endpoint | Sort-Object -Unique)){
        $rows=@($group.Group.operations | Where-Object {$_.endpoint -eq $endpoint -and $_.observed})
        if(!$rows.Count){continue}
        $values=@($rows.p95Ms | Sort-Object)
        $middle=[int][Math]::Floor($values.Count/2)
        $median=if($values.Count%2){$values[$middle]}else{($values[$middle-1]+$values[$middle])/2}
        @{configuration=$group.Name;endpoint=$endpoint;repetitions=$rows.Count;
            p95MedianMs=$median;p95MinimumMs=$values[0];p95MaximumMs=$values[-1];
            p99MaximumMs=($rows.p99Ms | Measure-Object -Maximum).Maximum;
            failureRateMaximum=($rows.failureRate | Measure-Object -Maximum).Maximum}
    }
})
Save-PerfJson @{runs=$runs;aggregates=$aggregates} (Join-Path $directory 'comparison.json')
$aggregates | ForEach-Object {[pscustomobject]$_} | Export-Csv (Join-Path $directory 'operations.csv') -NoTypeInformation -Encoding utf8
$lines=[Collections.Generic.List[string]]::new()
$lines.Add('# NoCTF 重复性能测量')
$lines.Add('')
$lines.Add('逐轮保留失败与诊断完整性；配置分组中的分位数是各轮 p95 的中位数和范围，不能视为合并全部请求后的 p95。比较应用程序集、脚本 SHA256、连接预算与采集配置后，才能判断运行是否等价。')
$lines.Add('')
$lines.Add('| 运行标识 | VU | 时间 | 负载 | Observability | 性能 | 业务 | 回收 | HTTP 失败率 | 请求/秒 |')
$lines.Add('|---|---:|---|---|---|---|---|---|---:|---:|')
foreach($run in $runs){$lines.Add("| $($run.runId) | $($run.vus) | $($run.duration) | $($run.workload) | $($run.observability) | $($run.performancePassed) | $($run.businessPassed) | $($run.cleanupPassed) | $([Math]::Round($run.failureRate*100,3))% | $([Math]::Round($run.requestsPerSecond,2)) |")}
$lines.Add('')
$lines.Add('| 配置：VU/时间/负载/Observability/诊断/程序集 | 接口 | 重复次数 | p95 中位数 ms | p95 最小 ms | p95 最大 ms | p99 最大 ms |')
$lines.Add('|---|---|---:|---:|---:|---:|---:|')
foreach($row in $aggregates){$lines.Add("| $($row.configuration.Replace('|','/')) | $($row.endpoint) | $($row.repetitions) | $([Math]::Round($row.p95MedianMs,2)) | $([Math]::Round($row.p95MinimumMs,2)) | $([Math]::Round($row.p95MaximumMs,2)) | $([Math]::Round($row.p99MaximumMs,2)) |")}
$lines | Set-Content (Join-Path $directory 'comparison.md') -Encoding utf8
Write-Host "比较报告：$directory"
