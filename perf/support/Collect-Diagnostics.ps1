#Requires -Version 7.4
param([Parameter(Mandatory)][string]$StateFile,[Parameter(Mandatory)][string]$ConfigFile,[ValidateRange(0,3600)][int]$CollectionSeconds=0)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Environment.ps1"
. "$PSScriptRoot/Diagnostics.ps1"
$state=Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json -AsHashtable
$config=Get-Content -LiteralPath $ConfigFile -Raw | ConvertFrom-Json -AsHashtable
$directory=Join-Path $state.reports 'diagnostics'
$perfRoot=Split-Path $PSScriptRoot
$children=[Collections.Generic.List[object]]::new()
$status=@{complete=$false;error=$null;samples=0;startedAt=[DateTimeOffset]::UtcNow.ToString('O');counterUtcOffsetMinutes=[DateTimeOffset]::Now.Offset.TotalMinutes;observability=$state.observability;traceRequested=$config.diagnosticsMode -eq 'Trace';traceCaptured=$false}
$status.collectorSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$status.metricsFailures=0
$status.postgresFailures=0

function Start-DiagnosticTool([string]$Name,[string]$Tool,[string[]]$Arguments) {
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $info.WorkingDirectory=$perfRoot
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardInput=$true
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    foreach($argument in (@('tool','run',$Tool,'--')+$Arguments)){ $info.ArgumentList.Add($argument) }
    Add-PerfRecord (Join-Path $directory 'commands.jsonl') @{utc=[DateTimeOffset]::UtcNow.ToString('O');executable=$info.FileName;arguments=@($info.ArgumentList)}
    $process=[Diagnostics.Process]::Start($info)
    $entry=@{name=$Name;process=$process;stdout=$process.StandardOutput.ReadToEndAsync();stderr=$process.StandardError.ReadToEndAsync()}
    $children.Add($entry)
    return $entry
}

function Complete-DiagnosticTool($Entry) {
    $process=$Entry.process
    $timedOut=$false
    if (!$process.HasExited) {
        if (!$process.WaitForExit(25000)) {
            $timedOut=$true
            $process.Kill($true)
            if(!$process.WaitForExit(5000)){throw "诊断工具无法终止：$($Entry.name)"}
        }
    }
    $Entry.stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $directory "$($Entry.name).log")
    $Entry.stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $directory "$($Entry.name)-error.log")
    if($timedOut){throw "诊断工具停止超时：$($Entry.name)"}
    if ($process.ExitCode -ne 0) { throw "诊断工具失败：$($Entry.name)，退出码 $($process.ExitCode)" }
    if($Entry.name -like 'counters-*'){
        $csv=Join-Path $directory "$($Entry.name).csv"
        if(!(Test-Path -LiteralPath $csv) -or $null -eq (Import-Csv -LiteralPath $csv | Select-Object -First 1)){
            throw "运行时计数器没有有效输出：$($Entry.name)，请检查对应工具日志。"
        }
    }
}

function Start-CounterSegment([int]$Index) {
    return Start-DiagnosticTool ("counters-{0:0000}" -f $Index) dotnet-counters @('collect','--process-id',"$($state.hostPid)",'--refresh-interval','1','--format','csv','--output',(Join-Path $directory ("counters-{0:0000}.csv" -f $Index)),'--counters',$providers,'--maxHistograms','256','--maxTimeSeries','4096','--duration','00:00:00:20')
}

function Start-PostgresConnection {
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command docker).Source)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in @('exec','-i','--env','PGOPTIONS=-c statement_timeout=10000',$state.containers.postgres,'psql','-X','-q','-U','noctf','-d','noctf','-A','-t','-v','ON_ERROR_STOP=1')){$info.ArgumentList.Add($argument)}
    Add-PerfRecord (Join-Path $directory 'commands.jsonl') @{utc=[DateTimeOffset]::UtcNow.ToString('O');executable=$info.FileName;arguments=@($info.ArgumentList)}
    $process=[Diagnostics.Process]::Start($info)
    return @{process=$process;stderr=$process.StandardError.ReadToEndAsync()}
}

function Get-PostgresSnapshot {
    if($postgresConnection.process.HasExited){throw 'PostgreSQL 持久诊断连接已经退出。'}
    $postgresConnection.process.StandardInput.WriteLine($snapshotSql)
    $postgresConnection.process.StandardInput.Flush()
    $read=$postgresConnection.process.StandardOutput.ReadLineAsync()
    if(!$read.Wait(15000)){throw 'PostgreSQL 诊断查询超过 15 秒。'}
    $json=$read.GetAwaiter().GetResult()
    if([string]::IsNullOrWhiteSpace($json)){throw 'PostgreSQL 诊断查询没有返回 JSON。'}
    return $json | ConvertFrom-Json -AsHashtable
}

function Read-AvailablePostgresSnapshot {
    if($null -eq $script:postgresConnection -and [DateTimeOffset]::UtcNow -lt $script:nextPostgresAttempt){return $null}
    try {
        if($null -eq $script:postgresConnection){$script:postgresConnection=Start-PostgresConnection}
        return Get-PostgresSnapshot
    } catch {
        $status.postgresFailures++
        Add-PerfRecord (Join-Path $directory 'probe-failures.jsonl') @{utc=[DateTimeOffset]::UtcNow.ToString('O');probe='postgres';failure='Unavailable'}
        if($null -ne $script:postgresConnection -and !$script:postgresConnection.process.HasExited){$script:postgresConnection.process.Kill($true)}
        $script:postgresConnection=$null
        $script:nextPostgresAttempt=[DateTimeOffset]::UtcNow.AddSeconds(30)
        return $null
    }
}

$lastCpu=@{}
function Get-ProcessSample([int]$ProcessId,[string]$Role) {
    $process=Get-Process -Id $ProcessId -ErrorAction Stop
    $now=[DateTimeOffset]::UtcNow
    $cpu=$process.TotalProcessorTime.TotalSeconds
    $cores=$null
    if ($lastCpu.ContainsKey($ProcessId)) {
        $elapsed=($now-$lastCpu[$ProcessId].utc).TotalSeconds
        if ($elapsed -gt 0) { $cores=($cpu-$lastCpu[$ProcessId].cpu)/$elapsed }
    }
    $lastCpu[$ProcessId]=@{utc=$now;cpu=$cpu}
    return @{role=$Role;pid=$ProcessId;cpuSeconds=$cpu;usedCpuCores=$cores;privateBytes=$process.PrivateMemorySize64;workingSetBytes=$process.WorkingSet64;threads=$process.Threads.Count}
}

Push-Location $perfRoot
$postgresConnection=$null
$nextPostgresAttempt=[DateTimeOffset]::MinValue
try {
    $snapshotSql=Get-Content "$PSScriptRoot/sql/snapshot.sql" -Raw
    $null=Read-AvailablePostgresSnapshot
    $providers='EventCounters\System.Runtime,Microsoft.EntityFrameworkCore,Microsoft.AspNetCore.Hosting,System.Net.Http,System.Net.Sockets,Npgsql'
    if ($state.observability) { $providers+=',NoCTF' }
    $maximumSeconds=[int]($config.durationSeconds+$config.vus*2+$config.warmupSeconds+180)
    $counterIndex=0
    $counters=Start-CounterSegment $counterIndex
    $trace=$null
    $stackCount=0
    $measurementStart=$null
    while (!(Test-Path (Join-Path $state.local 'diagnostics.stop')) -and ($CollectionSeconds -eq 0 -or [DateTimeOffset]::UtcNow -lt ([DateTimeOffset]$status.startedAt).AddSeconds($CollectionSeconds))) {
        $sampleStarted=[DateTimeOffset]::UtcNow
        if ($counters.process.HasExited) {
            Complete-DiagnosticTool $counters
            $counterIndex++
            $counters=Start-CounterSegment $counterIndex
        }
        if(!(Test-Path (Join-Path $state.local 'diagnostics.ready'))){
            $csv=Join-Path $directory "$($counters.name).csv"
            if((Test-Path -LiteralPath $csv) -and $null -ne (Import-Csv -LiteralPath $csv | Select-Object -First 1)){
                New-Item -ItemType File -Path (Join-Path $state.local 'diagnostics.ready') -Force | Out-Null
            }
        }
        if ($null -eq $measurementStart -and (Test-Path (Join-Path $state.local 'measurement.json'))) {
            $marker=Get-Content (Join-Path $state.local 'measurement.json') -Raw | ConvertFrom-Json -AsHashtable
            $measurementStart=[DateTimeOffset]$marker.utc
            if($measurementStart -lt ([DateTimeOffset]$status.startedAt).AddSeconds(-1) -or $measurementStart -gt [DateTimeOffset]::UtcNow.AddSeconds(1)){throw '测量阶段 UTC 时间无效。'}
        }
        if ($null -ne $measurementStart -and $config.diagnosticsMode -eq 'Trace' -and $null -eq $trace -and ($sampleStarted-$measurementStart).TotalSeconds -ge $config.traceDelaySeconds) {
            $available=[Math]::Floor($config.durationSeconds-($sampleStarted-$measurementStart).TotalSeconds-5)
            if($available -lt 1){throw '没有剩余测量窗口可用于轨迹采集。'}
            $actualTraceSeconds=[Math]::Min($config.traceSeconds,$available)
            $traceSpan=[TimeSpan]::FromSeconds($actualTraceSeconds)
            $traceDuration='{0:00}:{1:00}:{2:00}:{3:00}' -f $traceSpan.Days,$traceSpan.Hours,$traceSpan.Minutes,$traceSpan.Seconds
            # 不启用异常正文、DiagnosticSource 或数据库命令事件。
            $trace=Start-DiagnosticTool runtime-trace dotnet-trace @('collect','--process-id',"$($state.hostPid)",'--profile','dotnet-sampled-thread-time','--providers','Microsoft-Windows-DotNETRuntime:0x104003401D:5,NoCTF-Performance:0xFFFFFFFFFFFFFFFF:4','--buffersize','256','--duration',$traceDuration,'--output',(Join-Path $directory 'runtime.nettrace'))
            $status.traceStartedAt=$sampleStarted.ToString('O')
            $status.traceWindowSeconds=$actualTraceSeconds
        }
        if ($null -ne $trace -and $stackCount -lt 3) {
            $null=Start-DiagnosticTool "stacks-$stackCount" dotnet-stack @('report','--process-id',"$($state.hostPid)")
            $stackCount++
        }
        $processes=@(Get-ProcessSample $state.hostPid Host)
        $drivers=Get-CimInstance Win32_Process -Filter "Name = 'k6.exe'" | Where-Object { $_.CommandLine -like "*$($state.runId)*" }
        foreach($driver in $drivers){$processes+=Get-ProcessSample $driver.ProcessId k6}
        $postgres=Read-AvailablePostgresSnapshot
        $varz=Invoke-RestMethod "$($state.natsMonitorUrl)/varz" -TimeoutSec 10
        $jsz=Invoke-RestMethod "$($state.natsMonitorUrl)/jsz?accounts=true&streams=true&consumers=true" -TimeoutSec 10
        $dockerRaw=& docker stats --no-stream --format '{{json .}}' @($state.containers.Values)
        if($LASTEXITCODE -ne 0){throw '容器资源采样失败。'}
        $containers=@($dockerRaw | ForEach-Object { $_ | ConvertFrom-Json -AsHashtable })
        $redis=& docker exec $state.containers.redis redis-cli INFO stats
        if($LASTEXITCODE -ne 0){throw 'Redis 诊断查询失败。'}
        $phase=if($null -eq $measurementStart){'setup'}elseif(($sampleStarted-$measurementStart).TotalSeconds -lt $config.durationSeconds){'measurement'}else{'drain'}
        $sample=@{utc=$sampleStarted.ToString('O');phase=$phase;processes=$processes;containers=$containers;postgres=$postgres;nats=@{cpu=$varz.cpu;memory=$varz.mem;connections=$varz.connections;inMessages=$varz.in_msgs;outMessages=$varz.out_msgs;slowConsumers=$varz.slow_consumers;jetstream=$jsz};redisStats=($redis -join "`n")}
        if($state.observability){
            try {
                $metrics=Invoke-WebRequest $state.metricsUrl -TimeoutSec 3
                $metrics.Content | Set-Content (Join-Path $directory ("metrics-{0:0000}.prom" -f $status.samples)) -Encoding utf8
            } catch {
                $status.metricsFailures++
                $sample.metricsUnavailable=$true
                Add-PerfRecord (Join-Path $directory 'probe-failures.jsonl') @{utc=[DateTimeOffset]::UtcNow.ToString('O');probe='metrics';failure='Unavailable'}
            }
        }
        $sample.collectionMilliseconds=([DateTimeOffset]::UtcNow-$sampleStarted).TotalMilliseconds
        Add-PerfRecord (Join-Path $directory 'samples.jsonl') $sample
        $status.samples++
        if ([DateTimeOffset]::UtcNow -gt ([DateTimeOffset]$status.startedAt).AddSeconds($maximumSeconds)) { throw '诊断采集超过本次运行的时间上限。' }
        $remaining=$config.diagnosticsIntervalSeconds*1000-$sample.collectionMilliseconds
        if($remaining -gt 0){Start-Sleep -Milliseconds ([int]$remaining)}
    }
    foreach($child in $children){Complete-DiagnosticTool $child}
    if($config.diagnosticsMode -eq 'Trace'){
        if($null -eq $trace){throw '测量时间未覆盖轨迹采集延迟，没有生成运行时轨迹。'}
        Invoke-PerfNative dotnet @('tool','run','dotnet-trace','--','report',(Join-Path $directory 'runtime.nettrace'),'topN','--inclusive','--number','40') (Join-Path $directory 'top-inclusive.txt')
        Invoke-PerfNative dotnet @('tool','run','dotnet-trace','--','report',(Join-Path $directory 'runtime.nettrace'),'topN','--number','40') (Join-Path $directory 'top-exclusive.txt')
        Invoke-PerfNative dotnet @('tool','run','dotnet-trace','--','convert',(Join-Path $directory 'runtime.nettrace'),'--format','Speedscope','--output',(Join-Path $directory 'runtime')) (Join-Path $directory 'convert.log')
        Invoke-PerfNative dotnet @((Join-Path $perfRoot 'analysis/bin/Release/net10.0/NoCTF.PerfAnalysis.dll'),(Join-Path $directory 'runtime.nettrace'),$directory) (Join-Path $directory 'trace-analysis.log')
        $status.analyzerSha256=(Get-FileHash -LiteralPath (Join-Path $perfRoot 'analysis/bin/Release/net10.0/NoCTF.PerfAnalysis.dll') -Algorithm SHA256).Hash
        $analysis=Get-Content (Join-Path $directory 'trace-analysis.json') -Raw | ConvertFrom-Json -AsHashtable
        $status.eventsLost=$analysis.eventsLost
        $status.requestEvents=$analysis.requestCount
        $status.stageKinds=$analysis.stages.Count
        if($analysis.eventsLost -gt 0){throw "轨迹存在丢失事件：$($analysis.eventsLost)"}
        if($CollectionSeconds -eq 0 -and $state.observability -and ($analysis.requestCount -eq 0 -or $analysis.stages.Count -eq 0)){throw '轨迹缺少预期的请求或阶段事件。'}
        $status.traceCaptured=$true
    }
    Get-ChildItem $directory -Filter 'counters-*.csv' | Sort-Object Name | ForEach-Object {Import-Csv -LiteralPath $_.FullName} | Export-Csv (Join-Path $directory 'counters.csv') -NoTypeInformation -Encoding utf8
    $rows=@(Import-Csv (Join-Path $directory 'counters.csv'))
    if($rows.Count -eq 0 -or $status.samples -eq 0){throw '诊断文件缺少有效样本。'}
    $status.counterRows=$rows.Count
    $status.counterSegments=$counterIndex+1
    if($status.metricsFailures -gt 0){throw "指标端点采样失败 $($status.metricsFailures) 次；运行时及依赖证据已经保留。"}
    if($status.postgresFailures -gt 0){throw "PostgreSQL 采样失败 $($status.postgresFailures) 次；其他运行时及依赖证据已经保留。"}
    $status.complete=$true
} catch {
    $status.error=$_.Exception.Message
    throw
} finally {
    if($null -ne $postgresConnection){
        if(!$postgresConnection.process.HasExited){
            $postgresConnection.process.StandardInput.WriteLine('\q')
            $postgresConnection.process.StandardInput.Flush()
            if(!$postgresConnection.process.WaitForExit(5000)){$postgresConnection.process.Kill($true)}
        }
        $postgresConnection.stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $directory 'postgres-error.log')
    }
    foreach($child in $children){
        if(!$child.process.HasExited){
            if(!$child.process.WaitForExit(5000)){$child.process.Kill($true)}
        }
    }
    $status.endedAt=[DateTimeOffset]::UtcNow.ToString('O')
    Save-PerfJson $status (Join-Path $directory 'status.json')
    Pop-Location
}
