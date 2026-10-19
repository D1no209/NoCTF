#Requires -Version 7.4
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$RunId)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/support/Environment.ps1"
$reports=Join-Path $PSScriptRoot "results/$RunId"
$directory=Join-Path $reports 'diagnostics'
$config=Get-Content (Join-Path $reports 'configuration.json') -Raw | ConvertFrom-Json -AsHashtable
$summary=Get-Content (Join-Path $reports 'summary.json') -Raw | ConvertFrom-Json -AsHashtable
$inspection=@{runId=$RunId;observability=$config.observability;diagnosticsMode=$config.diagnosticsMode;requests=@();requestWindows=@();runtime=@();stages=@();statements=@();waits=@();limitations=[Collections.Generic.List[string]]::new()}
foreach($name in @($summary.metrics.Keys | Where-Object {$_ -match '^http_req_duration\{scenario:default,name:[a-z_]+\}$'})){
    $endpoint=[regex]::Match($name,'name:([a-z_]+)').Groups[1].Value
    if($summary.metrics.ContainsKey($name)){
        $values=$summary.metrics[$name].values
        $countKey="operation_requests{endpoint:$endpoint}"
        $failureKey="operation_failed{endpoint:$endpoint}"
        $count=if($summary.metrics.ContainsKey($countKey)){$summary.metrics[$countKey].values.count}else{$null}
        $failure=if($summary.metrics.ContainsKey($failureKey)){$summary.metrics[$failureKey].values.rate}else{$null}
        $inspection.requests+=@{endpoint=$endpoint;observed=$null -ne $count -and $count -gt 0;count=$count;requestsPerSecond=if($null -ne $count){$count/$config.durationSeconds}else{$null};failureRate=$failure;medianMs=if($count -gt 0){$values.med}else{$null};p95Ms=if($count -gt 0){$values.'p(95)'}else{$null};p99Ms=if($count -gt 0){$values.'p(99)'}else{$null};maxMs=if($count -gt 0){$values.max}else{$null}}
    }
}
foreach($name in @($summary.metrics.Keys | Where-Object {$_ -match '^http_req_duration\{scenario:default,name:[a-z_]+,window:(first_use|steady)\}$'})){
    $match=[regex]::Match($name,'name:([a-z_]+),window:(first_use|steady)')
    $endpoint=$match.Groups[1].Value;$window=$match.Groups[2].Value
    $countKey="operation_requests{endpoint:$endpoint,window:$window}"
    $count=$summary.metrics[$countKey].values.count
    if($count -gt 0){$inspection.requestWindows+=@{endpoint=$endpoint;window=$window;count=$count;p95Ms=$summary.metrics[$name].values.'p(95)';p99Ms=$summary.metrics[$name].values.'p(99)'}}
}
$inspection.httpFailureRate=$summary.metrics['http_req_failed{scenario:default}'].values.rate
$inspection.measurementRequests=$summary.metrics['http_reqs{scenario:default}'].values.count
$inspection.measurementRequestsPerSecond=$inspection.measurementRequests/$config.durationSeconds
$logPath=Join-Path $reports 'k6.log'
$inspection.failedSessions=if(Test-Path $logPath){@((Select-String -LiteralPath $logPath -Pattern '^PERF_SESSION_FAILED VU=(\d+),' | ForEach-Object {$_.Matches[0].Groups[1].Value}) | Sort-Object -Unique).Count}else{$null}
$inspection.sessions=@{}
foreach($name in @('participant_sessions_started','participant_sessions_completed','participant_sessions_failed','iterations{scenario:default}')){
    if($summary.metrics.ContainsKey($name)){$inspection.sessions[$name]=$summary.metrics[$name].values.count}
}
$failureValues=$summary.metrics['http_req_failed{scenario:default}'].values
$inspection.httpFailureMetricCoverage=($failureValues.passes+$failureValues.fails) -eq $summary.metrics['http_reqs{scenario:default}'].values.count
if(!$inspection.httpFailureMetricCoverage){$inspection.limitations.Add('HTTP 原生失败率的样本数与请求数不一致，不能将该失败率用于全部请求的验收。')}
$inspection.failedThresholds=@(foreach($name in $summary.metrics.Keys){
    $metric=$summary.metrics[$name]
    if($metric.ContainsKey('thresholds')){
        foreach($threshold in $metric.thresholds.Keys){if(!$metric.thresholds[$threshold].ok){"$name : $threshold"}}
    }
})
$status=$null
if($config.diagnosticsMode -ne 'None'){
    $status=Get-Content (Join-Path $directory 'status.json') -Raw | ConvertFrom-Json -AsHashtable
    $inspection.collectionComplete=$status.complete
    if(!$status.complete -and $status.error){$inspection.limitations.Add("诊断采集存在缺失：$($status.error)")}
    foreach($probe in @('metricsFailures','postgresFailures')){if($status.ContainsKey($probe) -and $status[$probe] -gt 0){$inspection.limitations.Add("$probe = $($status[$probe])，详见 probe-failures.jsonl。")}}
    if(!$status.complete){$inspection.limitations.Add("诊断采集不完整：$($status.error)")}
    $tracePath=Join-Path $directory 'trace-analysis.json'
    if(Test-Path $tracePath){
        $inspection.trace=Get-Content $tracePath -Raw | ConvertFrom-Json -AsHashtable
        $inspection.leaderboardWaitCorrelations=@(foreach($request in $inspection.trace.slowRequests){
            $waits=@($request.stages | Where-Object {$_.Area -eq 'Leaderboard' -and $_.Stage -eq 'KeyedLockWait'})
            if($request.Endpoint -match 'leaderboard|teams/me/standing' -and $waits.Count -eq 1 -and $request.DurationMs -gt 0 -and $waits[0].DurationMs -le $request.DurationMs){
                @{traceId=$request.TraceId;requestMs=$request.DurationMs;lockWaitMs=$waits[0].DurationMs;waitFraction=$waits[0].DurationMs/$request.DurationMs}
            }
        })
        if($inspection.trace.eventsLost -gt 0){$inspection.limitations.Add("原始轨迹丢失 $($inspection.trace.eventsLost) 个事件，轨迹分布与请求关联不完整。")}
        if($inspection.trace.requestCount -eq 0){$inspection.limitations.Add('轨迹窗口中没有请求事件，不能据此解释请求耗时。')}
    }
    $symbolsPath=Join-Path $directory 'top-inclusive.txt'
    if(Test-Path $symbolsPath){
        foreach($line in (Get-Content $symbolsPath | Where-Object {$_ -match 'Missing Symbol'})){$inspection.limitations.Add("采样方法报告存在未解析符号：$($line.Trim())")}
    }
    $counterPath=Join-Path $directory 'counters.csv'
    if($status.ContainsKey('counterUtcOffsetMinutes') -and (Test-Path $counterPath)){
        $offset=[TimeSpan]::FromMinutes($status.counterUtcOffsetMinutes)
        $marker=Get-Content "$PSScriptRoot/.local/$RunId/measurement.json" -Raw | ConvertFrom-Json -AsHashtable
        $start=[DateTimeOffset]$marker.utc
        $end=$start.AddSeconds($config.durationSeconds)
        $rows=@(Import-Csv $counterPath | Where-Object {
            $stamp=[DateTime]::SpecifyKind([DateTime]::Parse($_.Timestamp,[Globalization.CultureInfo]::InvariantCulture),[DateTimeKind]::Unspecified)
            $utc=[DateTimeOffset]::new($stamp,$offset)
            $utc -ge $start -and $utc -le $end
        })
        foreach($group in ($rows | Group-Object Provider,'Counter Name')){
            $numbers=@(foreach($row in $group.Group){
                $number=0.0
                if([double]::TryParse($row.'Mean/Increment',[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number) -and [double]::IsFinite($number)){$number}
            })
            if($numbers.Count -eq 0){continue}
            $stats=$numbers | Measure-Object -Minimum -Maximum -Average
            $item=@{provider=$group.Group[0].Provider;counter=$group.Group[0].'Counter Name';samples=$numbers.Count;minimum=$stats.Minimum;maximum=$stats.Maximum;average=$stats.Average}
            if($item.provider -eq 'NoCTF' -and $item.counter -match 'performance.stage|admission.transactions|gameplay_fact.stage|leaderboard.projection'){$inspection.stages+=$item}
            elseif($item.provider -in @('System.Runtime','Npgsql','Microsoft.AspNetCore.Hosting')){$inspection.runtime+=$item}
        }
    }else{$inspection.limitations.Add('缺少合并计数器文件或 UTC 偏移，不能可靠关联计数器测量阶段。')}
    $samples=@(Get-Content (Join-Path $directory 'samples.jsonl') | ForEach-Object {$_ | ConvertFrom-Json -AsHashtable})
    $measured=@($samples | Where-Object phase -eq 'measurement')
    $inspection.dependencySamples=$measured.Count
    $databaseSamples=@($measured | Where-Object {$null -ne $_.postgres})
    if($databaseSamples.Count -ge 2){
        $first=$databaseSamples[0];$last=$databaseSamples[-1]
        $inspection.database=@{commits=$last.postgres.database.xact_commit-$first.postgres.database.xact_commit;rollbacks=$last.postgres.database.xact_rollback-$first.postgres.database.xact_rollback;deadlocks=$last.postgres.database.deadlocks-$first.postgres.database.deadlocks;tempBytes=$last.postgres.database.temp_bytes-$first.postgres.database.temp_bytes;windowStart=$first.utc;windowEnd=$last.utc}
        $baseline=@{}
        foreach($statement in $first.postgres.statements){$baseline[$statement.queryid]=$statement}
        foreach($statement in $last.postgres.statements){
            if(!$baseline.ContainsKey($statement.queryid)){continue}
            $previous=$baseline[$statement.queryid]
            $calls=$statement.calls-$previous.calls
            if($calls -le 0){continue}
            $inspection.statements+=@{queryId=$statement.queryid;calls=$calls;planningMs=$statement.total_plan_time-$previous.total_plan_time;executionMs=$statement.total_exec_time-$previous.total_exec_time;rows=$statement.rows-$previous.rows;blocksRead=$statement.shared_blks_read-$previous.shared_blks_read;temporaryBlocks=$statement.temp_blks_written-$previous.temp_blks_written}
        }
        $inspection.statements=@($inspection.statements | Sort-Object executionMs -Descending | Select-Object -First 15)
        $activities=@(foreach($sample in $databaseSamples){foreach($activity in $sample.postgres.activity){if($activity.state -eq 'active' -and $null -ne $activity.wait_event_type){$activity}}})
        $inspection.waits=@($activities | Group-Object wait_event_type,wait_event | Sort-Object Count -Descending | ForEach-Object {@{wait=$_.Name;connectionSamples=$_.Count}})
        $lockSamples=@(foreach($sample in $databaseSamples){foreach($lock in $sample.postgres.locks){if(!$lock.granted){$lock}}})
        $inspection.waitingLocks=@($lockSamples | Group-Object relation,mode | Sort-Object Count -Descending | ForEach-Object {@{relationAndMode=$_.Name;snapshots=$_.Count}})
    }else{$inspection.limitations.Add('测量阶段依赖样本不足，不能计算数据库增量。')}
    $inspection.processes=@(foreach($role in @('Host','k6')){
        $values=@(foreach($sample in $measured){$sample.processes | Where-Object role -eq $role})
        if($values.Count -gt 0){@{role=$role;peakCpuCores=($values.usedCpuCores | Measure-Object -Maximum).Maximum;peakPrivateBytes=($values.privateBytes | Measure-Object -Maximum).Maximum;peakWorkingSetBytes=($values.workingSetBytes | Measure-Object -Maximum).Maximum}}
    })
    $inspection.limitations.Add('计数器使用有界采集段，段间存在工具重新连接间隔；CSV 时间精度为秒。')
    $inspection.limitations.Add('依赖采集器占用一个持久 PostgreSQL 连接；数据库容量判断需要计入这一诊断开销。')
    $inspection.limitations.Add('阶段指标为各一秒区间内的分位数，报告展示区间峰值；嵌套阶段不能相加。')
    $inspection.limitations.Add('运行时原始序列保留工具给出的 Mean/Increment 语义；带 /1 sec 的增量序列不能直接解释为当前并发数量。线程数量优先核对 EventCounters 的 ThreadPool Thread Count 和进程线程采样。')
    $inspection.limitations.Add('数据库只输出 queryId、计数和等待；Top 50 快照交集不能代表全部数据库语句。')
    $inspection.limitations.Add('dotnet-sampled-thread-time 表示采样线程时间，需要结合进程 CPU 与等待证据解释，不能直接当作纯 CPU 占用。')
    if($config.diagnosticsMode -eq 'Trace'){$inspection.limitations.Add('包含有界采样轨迹及三次线程快照的额外开销；缺少符号或事件丢失需检查工具原始日志。')}
}else{
    $inspection.collectionComplete=$false
    $inspection.limitations.Add('本次 DiagnosticsMode=None，未采集运行时或依赖证据。')
}
if(!$config.observability){$inspection.limitations.Add('Observability 已关闭，应用 Prometheus 指标与服务端请求关联事件不可用。')}
$evidencePath=Join-Path $reports 'request-evidence.jsonl'
$inspection.requestEvidence=if(Test-Path $evidencePath){@(Get-Content $evidencePath | ForEach-Object {$_ | ConvertFrom-Json -AsHashtable} | Sort-Object durationMs -Descending | Select-Object -First 10)}else{@()}
$inspection.limitations.Add('请求关联记录每个 VU 最多保留十条慢请求和十条失败请求，不能用其数量估计错误率。')
$inspection.limitations.Add('本次为单次诊断运行，不据此宣称优化收益；前后对比应保持参数、Observability 和采集模式一致，重复至少三次。')
$verificationFile=Join-Path $reports 'verification.json'
if(Test-Path -LiteralPath $verificationFile){
    $verification=Get-Content -LiteralPath $verificationFile -Raw | ConvertFrom-Json -AsHashtable
    if($verification.ContainsKey('databaseCompletion')){$inspection.databaseCompletion=$verification.databaseCompletion}
}
Save-PerfJson $inspection (Join-Path $directory 'inspection.json')
$lines=[Collections.Generic.List[string]]::new()
$lines.Add("# NoCTF 性能诊断：$RunId")
$lines.Add('')
$lines.Add("负载：$($config.vus) VU，$($config.duration)；预热 $($config.warmupSeconds) 秒；Observability=$($config.observability)；DiagnosticsMode=$($config.diagnosticsMode)。")
$lines.Add('')
$lines.Add("HTTP 失败率：$([Math]::Round($inspection.httpFailureRate*100,3))%。诊断采集完整：$($inspection.collectionComplete)。")
$lines.Add("正式请求 $($inspection.measurementRequests)，按测量时长计算 $([Math]::Round($inspection.measurementRequestsPerSecond,2)) 请求/秒；失败后停止业务动作的会话 $($inspection.failedSessions)。k6 原生 Counter rate 的时间分母包含 setup，正式负载吞吐量使用这里的计数/测量时长。")
if($inspection.ContainsKey('databaseCompletion') -and $inspection.databaseCompletion.count -gt 0){
    $completion=$inspection.databaseCompletion
    $lines.Add("数据库完成：$($completion.count) 次，p50 $([Math]::Round($completion.p50Ms,2)) ms，p95 $([Math]::Round($completion.p95Ms,2)) ms，p99 $([Math]::Round($completion.p99Ms,2)) ms，超过 30 秒 $($completion.overThirtySeconds) 次。该统计独立于客户端观察耗时，分位数使用 nearest-rank。")
}
$lines.Add('')
$lines.Add('## HTTP 请求分布')
$lines.Add('')
$lines.Add('| 接口 | 请求数量 | 请求/秒 | 失败率 | 中位数 ms | p95 ms | p99 ms | 最大值 ms |')
$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|')
foreach($row in ($inspection.requests | Sort-Object p95Ms -Descending)){
    if(!$row.observed){$lines.Add("| $($row.endpoint) | 0 | 0 | — | 未覆盖 | — | — | — |");continue}
    $lines.Add("| $($row.endpoint) | $($row.count) | $([Math]::Round($row.requestsPerSecond,2)) | $([Math]::Round($row.failureRate*100,2))% | $([Math]::Round($row.medianMs,2)) | $([Math]::Round($row.p95Ms,2)) | $([Math]::Round($row.p99Ms,2)) | $([Math]::Round($row.maxMs,2)) |")
}
$lines.Add('')
$lines.Add('## 页面操作与实时会话')
$lines.Add('')
foreach($metricName in ($summary.metrics.Keys | Where-Object {$_ -match '^action_duration\{|^hub_call_duration|^hub_connections|^hub_failures$|^hub_events$|^scoreboard_event_to_snapshot$|^scoreboard_snapshot_age$|^scoreboard_response_cache_hits$|^token_refreshes$|^requests_after_initial_token_expiry$'} | Sort-Object)){
    $values=$summary.metrics[$metricName].values
    $lines.Add("- $metricName ：$(ConvertTo-Json $values -Compress)")
}
$lines.Add('')
$lines.Add('## 测量开始阶段与稳定阶段')
$lines.Add('')
$lines.Add('first_use 为测量开始后的前 30 秒，steady 为其余时间。此分组描述负载时间窗口；某个操作首次被调用时仍可能位于 steady。分组的 p(95)>=0 仅用于保留分位数，不代表延迟验收阈值。')
$lines.Add('')
$lines.Add('| 接口 | 窗口 | 请求数量 | p95 ms | p99 ms |')
$lines.Add('|---|---|---:|---:|---:|')
foreach($row in ($inspection.requestWindows | Sort-Object endpoint,window)){$lines.Add("| $($row.endpoint) | $($row.window) | $($row.count) | $([Math]::Round($row.p95Ms,2)) | $([Math]::Round($row.p99Ms,2)) |")}
$lines.Add('')
$lines.Add('## 原始轨迹窗口的阶段分布')
$lines.Add('')
if($inspection.ContainsKey('trace')){
    $lines.Add("解析事件 $($inspection.trace.eventCount)，丢失事件 $($inspection.trace.eventsLost)，请求事件 $($inspection.trace.requestCount)。")
    if($inspection.trace.ContainsKey('realtimeTransportCount')){$lines.Add("其中 HTTP 操作事件 $($inspection.trace.operationRequestCount)，实时传输存续事件 $($inspection.trace.realtimeTransportCount)；长连接存续时间单独保存在 realtimeSessions。")}
    $lines.Add('')
    $lines.Add('| 区域 | 阶段 | 数量 | 中位数 ms | p95 ms | p99 ms |')
    $lines.Add('|---|---|---:|---:|---:|---:|')
    foreach($row in $inspection.trace.stages){$lines.Add("| $($row.area) | $($row.stage) | $($row.count) | $([Math]::Round($row.medianMs,2)) | $([Math]::Round($row.p95Ms,2)) | $([Math]::Round($row.p99Ms,2)) |")}
    if($inspection.trace.ContainsKey('operations')){
        $lines.Add('')
        $lines.Add('### 热点操作的数据库成本')
        $lines.Add('')
        $lines.Add('仅关联同一 TraceId 且完成于该 HTTP 请求时间窗口内的数据库事件。位于采集边界的请求可能缺少阶段；提交路径中并行启动的关联后台工作可能包含在同一时间窗口内。查询执行与连接打开耗时分别统计。')
        $lines.Add('')
        $lines.Add('| 路由 | 请求样本 | 平均 SQL 数量 | SQL 数量 p95 | 累计 SQL 执行 p95 ms | 累计连接打开 p95 ms |')
        $lines.Add('|---|---:|---:|---:|---:|---:|')
        foreach($row in $inspection.trace.operations){$lines.Add("| $($row.endpoint) | $($row.requests) | $([Math]::Round($row.queriesMean,2)) | $([Math]::Round($row.queriesP95,2)) | $([Math]::Round($row.queryDurationP95Ms,2)) | $([Math]::Round($row.connectionOpenP95Ms,2)) |")}
    }
    $lines.Add('')
    $lines.Add('`diagnostics/trace-analysis.json` 中的 slowRequests 保存每个慢请求的 TraceId、路由、总耗时和同一 TraceId 的阶段事件；`performance-events.jsonl` 保留安全事件时间线。')
    if($inspection.leaderboardWaitCorrelations.Count -gt 0){
        $lines.Add('')
        $lines.Add('### 同一排行读取请求的等待构成')
        $lines.Add('')
        $lines.Add('| TraceId | 请求 ms | 本地锁等待 ms | 等待占比 |')
        $lines.Add('|---|---:|---:|---:|')
        foreach($row in ($inspection.leaderboardWaitCorrelations | Select-Object -First 10)){$lines.Add("| $($row.traceId) | $([Math]::Round($row.requestMs,2)) | $([Math]::Round($row.lockWaitMs,2)) | $([Math]::Round($row.waitFraction*100,2))% |")}
    }
}
$lines.Add('')
$lines.Add('## 应用阶段的一秒区间指标峰值')
$lines.Add('')
$lines.Add('| 原始指标及维度 | 样本数 | 区间指标最大值 |')
$lines.Add('|---|---:|---:|')
foreach($row in ($inspection.stages | Sort-Object maximum -Descending)){$lines.Add("| $($row.counter.Replace('|','\|')) | $($row.samples) | $([Math]::Round($row.maximum,6)) |")}
if($inspection.ContainsKey('database')){
    $lines.Add('')
    $lines.Add('## 数据库与进程')
    $lines.Add('')
    $lines.Add("测量采样窗口内提交 $($inspection.database.commits)，回滚 $($inspection.database.rollbacks)，死锁 $($inspection.database.deadlocks)。回滚数量需要结合事务失败指标区分业务事务和准入事务。")
    $lines.Add('')
    foreach($row in $inspection.processes){$lines.Add("- $($row.role)：CPU 峰值 $([Math]::Round($row.peakCpuCores,2)) 个核心，私有内存峰值 $([Math]::Round($row.peakPrivateBytes/1MB,2)) MiB。")}
    $lines.Add('')
    foreach($row in $inspection.waits){$lines.Add("- 数据库等待 $($row.wait)：$($row.connectionSamples) 个连接采样点。")}
    foreach($row in $inspection.waitingLocks){$lines.Add("- 未授予锁 $($row.relationAndMode)：$($row.snapshots) 个采样记录。")}
    $lines.Add('')
    $lines.Add('| QueryId | 调用增量 | 累计规划 ms 增量 | 累计执行 ms 增量 | 返回或影响行数增量 |')
    $lines.Add('|---|---:|---:|---:|---:|')
    foreach($row in $inspection.statements){$lines.Add("| $($row.queryId) | $($row.calls) | $([Math]::Round($row.planningMs,2)) | $([Math]::Round($row.executionMs,2)) | $($row.rows) |")}
}
$lines.Add('')
$lines.Add('## 请求关联样本')
$lines.Add('')
foreach($row in $inspection.requestEvidence){$lines.Add("- $($row.utc)：$($row.endpoint)，HTTP $($row.status)，$([Math]::Round($row.durationMs,2)) ms，TraceId=$($row.traceId)。")}
$lines.Add('')
$lines.Add('## 证据定位')
$lines.Add('')
$lines.Add('- `phases.jsonl`：准备、认证、预热、测量、结束和验证的 UTC 阶段标记。')
$lines.Add('- `diagnostics/counters.csv`：运行时、线程池、GC、连接池及应用阶段指标；`samples.jsonl`：进程、容器、数据库等待、NATS 和 Redis。')
$lines.Add('- `diagnostics/runtime.nettrace`：用 PerfView 查看 `NoCTF-Performance/RequestCompleted` 与 `StageCompleted`，按 TraceId 关联请求。')
$lines.Add('- `top-inclusive.txt`、`top-exclusive.txt`、`runtime.speedscope.json` 和 `stacks-*.log`：方法、调用关系和线程等待证据。')
$lines.Add('- `environment.json`、工具版本、`commands.jsonl`、匹配的 DLL/PDB 和原始工具日志保留在 diagnostics。')
$lines.Add('')
$lines.Add('## 测量限制')
$lines.Add('')
foreach($limitation in $inspection.limitations){$lines.Add("- $limitation")}
$lines | Set-Content (Join-Path $reports 'inspection.md') -Encoding utf8
Write-Host "诊断索引：$(Join-Path $reports 'inspection.md')"
