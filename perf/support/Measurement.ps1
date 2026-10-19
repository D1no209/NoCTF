function New-PerfWorkloadSnapshot($State, $Configuration, [string]$PerfRoot) {
    $snapshot=Join-Path $State.local 'workload'
    $scripts=Join-Path $snapshot 'scripts'
    $protocol=Join-Path $snapshot '.local'
    $null=New-Item -ItemType Directory -Path $scripts,$protocol -Force
    Get-ChildItem (Join-Path $PerfRoot 'scripts') -File | Copy-Item -Destination $scripts
    Copy-Item (Join-Path $PerfRoot '.local/signalr-protocol.js') (Join-Path $protocol 'signalr-protocol.js')
    $Configuration.scriptHashes=@(Get-ChildItem $snapshot -Recurse -File | ForEach-Object {@{name=[IO.Path]::GetRelativePath($snapshot,$_.FullName);sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}})
    return Join-Path $scripts 'noctf-performance.js'
}

function Invoke-PerfMeasurement($State, $Configuration, [string]$ConfigPath, [string]$Script, $Data, [string]$PerfRoot) {
    Start-PerfDiagnostics $State $Configuration $ConfigPath $PerfRoot
    $Configuration.k6StartedAt=[DateTimeOffset]::UtcNow.ToString('O')
    & k6 --log-format raw run --summary-mode full --vus $Configuration.vus --duration $Configuration.duration -e "PERF_CONFIG=$ConfigPath" $Script 2>&1 | ForEach-Object {
        $line="$_"
        $line | Add-Content (Join-Path $State.reports 'k6.log') -Encoding utf8
        if($line.StartsWith('PERF_PHASE ')){
            $marker=$line.Substring(11) | ConvertFrom-Json -AsHashtable
            Write-PerfPhase $State $marker.phase $marker.utc
        }elseif($line.StartsWith('PERF_REQUEST ')){
            $evidence=$line.Substring(13) | ConvertFrom-Json -AsHashtable
            Add-PerfRecord (Join-Path $State.reports 'request-evidence.jsonl') $evidence
        }
        Write-Output $line
    }
    $Configuration.k6ExitCode=$LASTEXITCODE
    $Configuration.k6EndedAt=[DateTimeOffset]::UtcNow.ToString('O')
    Save-PerfJson $Configuration (Join-Path $State.reports 'configuration.json')
    $diagnosticsError=$null
    try { Stop-PerfDiagnostics $State } catch { $diagnosticsError=$_.Exception.Message }
    # 采集失败仍执行业务验证，避免丢失已经受理的操作和计分证据。
    Write-PerfPhase $State verification
    $verification=Verify-PerfTestResults $State $Data
    Save-PerfJson $verification (Join-Path $State.reports 'verification.json')
    & (Join-Path $PerfRoot 'Inspect-Performance.ps1') -RunId $State.runId
    if($null -ne $diagnosticsError){throw $diagnosticsError}
    if($Configuration.k6ExitCode -ne 0 -or !$verification.passed){throw "性能测试验收失败，k6 退出码 $($Configuration.k6ExitCode)。报告：$($State.reports)"}
    Write-Host "性能测试通过。报告：$($State.reports)"
}
