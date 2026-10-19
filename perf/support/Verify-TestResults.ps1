function Verify-PerfTestResults($State, $Data) {
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $facts=[Collections.Generic.List[object]]::new()
        $offset=0
        do {
            $page=Invoke-PerfHttp $State GET "/api/v1/admin/competitions/$($Data.competitionId)/gameplay-facts?Offset=$offset&Limit=200" -Token ADMIN
            foreach ($fact in $page.items) { $facts.Add($fact) }
            $offset += $page.items.Count
        } while ($offset -lt $page.total -and $page.items.Count -gt 0)
        if (@($facts | Where-Object { $_.state -in @('Pending','Queued','Processing') }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    $errors=[Collections.Generic.List[string]]::new()
    $users=@{}
    foreach ($user in $Data.users) { $users[$user.teamId]=$user }
    $solved=@{}
    foreach ($fact in $facts) {
        if ($fact.kind -ne 'FlagAttempt') { continue }
        if (!$users.ContainsKey($fact.teamId) -or $users[$fact.teamId].userId -ne $fact.actorUserId) { $errors.Add("GameplayFact 身份不一致：$($fact.id)"); continue }
        $expected=if ($fact.competitionChallengeId -eq $Data.challenges[0].id) { 'Correct' } elseif ($fact.competitionChallengeId -eq $Data.challenges[1].id) { 'Wrong' } else { 'InvalidTarget' }
        if ($fact.state -ne 'Completed' -or $fact.result -ne $expected) { $errors.Add("GameplayFact 结果不符合预期：$($fact.id)，$($fact.state)/$($fact.result)") }
        if ($fact.state -eq 'Completed' -and $expected -eq 'Correct' -and $fact.result -eq 'Correct') { $solved[$fact.teamId]=$true }
    }
    $board=$null
    do {
        $response=Invoke-WebRequest "$($State.baseUrl)/api/v1/competitions/$($Data.competitionId)/leaderboard" -SkipHttpErrorCheck
        if ($response.StatusCode -eq 200) {
            $board=$response.Content | ConvertFrom-Json -AsHashtable
            $matched=@($board.teams | Where-Object { $_.totalScore -eq $(if ($solved.ContainsKey($_.teamId)) {100} else {0}) }).Count
            if ($board.teams.Count -eq $users.Count -and $matched -eq $users.Count) { break }
        } elseif ($response.StatusCode -ne 202) { throw "最终排行榜返回 $($response.StatusCode)。" }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($null -eq $board -or $board.teams.Count -ne $users.Count) { $errors.Add('最终排行榜队伍数量不一致。') }
    if ($null -ne $board) {
        foreach ($team in $board.teams) {
            $expected=if ($solved.ContainsKey($team.teamId)) {100} else {0}
            if (!$users.ContainsKey($team.teamId) -or $team.totalScore -ne $expected) { $errors.Add("排行榜计分不一致：$($team.teamId)") }
        }
    }
    $summaryPath=Join-Path $State.reports 'summary.json'
    if (!(Test-Path -LiteralPath $summaryPath)) { $errors.Add('缺少 k6 JSON 报告。') }
    else {
        $summary=Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -AsHashtable
        $httpFailures=$summary.metrics['http_req_failed{scenario:default}'].values
        if($httpFailures.passes+$httpFailures.fails -ne $summary.metrics['http_reqs{scenario:default}'].values.count){$errors.Add('HTTP 原生失败率样本没有覆盖全部正式请求。')}
        $accepted=if ($summary.metrics.ContainsKey('submissions_accepted')) {$summary.metrics.submissions_accepted.values.count} else {0}
        $observed=0
        foreach ($metric in @('correct_completed','wrong_completed')) {
            if ($summary.metrics.ContainsKey($metric)) { $observed += $summary.metrics[$metric].values.count }
        }
        if (@($facts | Where-Object kind -eq 'FlagAttempt').Count -ne $accepted) { $errors.Add('受理提交数量与数据库记录不一致。') }
        if ($observed -ne $accepted) { $errors.Add('存在未在 k6 中观测到预期完成结果的受理提交。') }
    }
    $latencies=@($facts | Where-Object { $_.kind -eq 'FlagAttempt' -and $_.state -eq 'Completed' } | ForEach-Object {
        ([DateTimeOffset]$_.updatedAt-[DateTimeOffset]$_.occurredAt).TotalMilliseconds
    } | Sort-Object)
    $processing=@{count=$latencies.Count;percentileMethod='nearest-rank';measurement='OccurredAt to persisted Completed UpdatedAt'}
    if($latencies.Count){
        $processing.minimumMs=$latencies[0];$processing.maximumMs=$latencies[-1]
        $processing.p50Ms=$latencies[[Math]::Ceiling($latencies.Count*0.5)-1]
        $processing.p95Ms=$latencies[[Math]::Ceiling($latencies.Count*0.95)-1]
        $processing.p99Ms=$latencies[[Math]::Ceiling($latencies.Count*0.99)-1]
        $processing.overFiveSeconds=@($latencies | Where-Object {$_ -gt 5000}).Count
        $processing.overThirtySeconds=@($latencies | Where-Object {$_ -gt 30000}).Count
    }
    return @{passed=$errors.Count -eq 0;competitionId=$Data.competitionId;teams=$users.Count;solvedTeams=$solved.Count;facts=$facts.Count;databaseCompletion=$processing;errors=$errors.ToArray();verifiedAt=[DateTimeOffset]::UtcNow.ToString('O')}
}
