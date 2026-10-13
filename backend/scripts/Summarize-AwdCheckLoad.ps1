[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)

$ErrorActionPreference = 'Stop'

function Get-Percentile($Values, [double]$Percentile) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    return [math]::Round($sorted[[math]::Max(0, [math]::Ceiling($sorted.Count * $Percentile) - 1)], 2)
}

$summaries = foreach ($metadataFile in Get-ChildItem -LiteralPath $Directory -Recurse -Filter run.json) {
    $metadata = Get-Content -LiteralPath $metadataFile.FullName | ConvertFrom-Json
    $evidenceFiles = @(Get-ChildItem -LiteralPath $metadataFile.DirectoryName -Filter 'awd-checks-*.jsonl')
    if ($evidenceFiles.Count -eq 0) {
        [pscustomobject]@{ directory = $metadataFile.DirectoryName; exitCode = $metadata.e2eExitCode; measured = $false }
        continue
    }
    $rows = @($evidenceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName } | ConvertFrom-Json)
    $evidence = @($rows | Where-Object operation -eq evidence)
    $latest = @($evidence | Group-Object { $_.evidence.gameplayFactId } | ForEach-Object { $_.Group[-1] })
    $pending = @($latest | Where-Object { $_.evidence.settlement -eq 'PendingAdjudication' })
    # Finishing a competition holds unstarted samples, including future samples. Do not attribute those to load.
    $endedBeforeDeadline = @($pending | Where-Object {
        $_.evidence.reason -eq 'TaskNotExecuted' -and $_.capturedAt -lt $_.evidence.deadline
    })
    $failures = @($pending | Where-Object { $_ -notin $endedBeforeDeadline })
    $attempts = @($evidence | Where-Object { $_.evidence.attemptStartedAt -and $_.evidence.observedAt } |
        Group-Object { "$($_.evidence.gameplayFactId)/$($_.evidence.attemptId)" } | ForEach-Object { $_.Group[-1].evidence })
    $executionMs = @($attempts | ForEach-Object { ($_.observedAt - $_.attemptStartedAt).TotalMilliseconds })
    $listMs = @($rows | Where-Object operation -eq list | ForEach-Object durationMs)
    $counterFile = Join-Path $metadataFile.DirectoryName 'runner-counters.csv'
    $queue = if (Test-Path -LiteralPath $counterFile) {
        @(Import-Csv -LiteralPath $counterFile | Where-Object 'Counter Name' -Like '*thread_pool.queue*' |
            ForEach-Object { [double]$_.'Mean/Increment' })
    } else { @() }
    [pscustomobject]@{
        directory = $metadataFile.DirectoryName
        imageId = $metadata.imageId
        exitCode = $metadata.e2eExitCode
        measured = $true
        samplesObserved = $latest.Count
        loadPending = $failures.Count
        loadPendingFinal = @($failures | Where-Object { $_.evidence.finalScheduledCheck }).Count
        reasons = @($failures | Group-Object { $_.evidence.reason } | ForEach-Object { @{ reason = $_.Name; count = $_.Count } })
        endedBeforeDeadline = $endedBeforeDeadline.Count
        provisionalAtLastCapture = @($latest | Where-Object { $_.evidence.settlement -eq 'Provisional' }).Count
        observedAttempts = $attempts.Count
        attemptObservationMsP50 = Get-Percentile $executionMs 0.50
        attemptObservationMsP95 = Get-Percentile $executionMs 0.95
        listMsP50 = Get-Percentile $listMs 0.50
        listMsP95 = Get-Percentile $listMs 0.95
        runnerThreadPoolQueueMax = ($queue | Measure-Object -Maximum).Maximum
    }
}
$summaries | ConvertTo-Json -Depth 6
