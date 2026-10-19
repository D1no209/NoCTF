function Prepare-PerfTestData($State, [int]$Vus, [double]$DurationSeconds) {
    $competition = Invoke-PerfHttp $State POST '/api/v1/admin/competitions' @{
        title="perf-$($State.runId)"; description='k6 performance test'; mode='Ctf'
        startTime=[DateTimeOffset]::UtcNow.AddHours(6).ToString('O')
        endTime=[DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds+86400).ToString('O')
        teamRegistrationAutoApprove=$true; allowTeamRegistrationWhileRunning=$true
        maxTeamMembers=1; maxConcurrentRuntimeInstancesPerTeam=0
    } ADMIN 201
    $competitionId = $competition.id
    $challenges = @()
    foreach ($kind in @('correct','wrong')) {
        $flag = "flag{$($State.runId)-$kind-$([guid]::NewGuid().ToString('N'))}"
        $template = Invoke-PerfHttp $State POST '/api/v1/admin/challenges' @{
            mode='Ctf'; visibility='Private'; title="perf-$($State.runId)-$kind"; description='Static attachment performance fixture'; direction='Misc'
            definition=@{mode='Ctf';ctf=@{interactionKind='FlagSubmission'}}
        } ADMIN 201
        $null = Invoke-PerfHttp $State POST "/api/v1/admin/challenges/$($template.id)/flags" @{flag=$flag;matchKind='Exact'} ADMIN 201
        $attachmentPath = Join-Path $State.local "$kind.txt"
        "NoCTF performance attachment`n$flag" | Set-Content -LiteralPath $attachmentPath -Encoding utf8
        $attachment = Invoke-PerfHttp $State POST "/api/v1/admin/challenges/$($template.id)/attachments" -Token ADMIN -Expected 201 -Form @{DeliveryPolicy='All';Files=(Get-Item -LiteralPath $attachmentPath)}
        $challenge = Invoke-PerfHttp $State POST "/api/v1/admin/competitions/$competitionId/challenges" @{challengeId=$template.id;order=$challenges.Count} ADMIN 201
        $null = Invoke-PerfHttp $State PATCH "/api/v1/admin/competitions/$competitionId/challenges/$($challenge.id)" @{
            presentation=@{customTitle=$null;order=$challenges.Count;isPublished=$true}
            rules=@{configuration=@{mode='Ctf';ctf=@{scoreCurve=@{initialPoints=100;minimumPoints=100;decayTeamCount=10;decayMode='Fixed';customExpression=$null};bloodRewards=@();maxFlagAttempts=$null}}}
        } ADMIN
        $challenges += @{id=$challenge.id;templateId=$template.id;attachmentId=$attachment.items[0].id;flag=$flag}
    }
    $null = Invoke-PerfHttp $State PUT "/api/v1/admin/competitions/$competitionId/status" @{status='Visible'} ADMIN 204
    $users = [Collections.Generic.List[object]]::new()
    for ($index=0; $index -lt $Vus; $index++) {
        $username = "perf-$($State.runId)-$index"
        $registered = Invoke-PerfHttp $State POST '/api/v1/auth/register' @{userName=$username;email="$username@perf.test";password=$State.password} -Expected 201
        $login = Invoke-PerfHttp $State POST '/api/v1/auth/login' @{login=$username;password=$State.password}
        if ($login.state -ne 'Authenticated') { throw "选手认证未完成：$index" }
        $team = Invoke-PerfHttp $State POST "/api/v1/competitions/$competitionId/teams" @{name=$username} $login.accessToken 201
        $approved = Invoke-PerfHttp $State PATCH "/api/v1/competitions/$competitionId/teams/$($team.id)" @{registration=@{status='Pending'}} $login.accessToken
        $membership = Invoke-PerfHttp $State GET "/api/v1/competitions/$competitionId/teams/me" -Token $login.accessToken
        if ($membership.registrationStatus -ne 'Approved' -or $membership.captainId -ne $registered.userId -or $membership.id -ne $team.id) { throw "队伍资格验证失败：$index" }
        $users.Add(@{login=$username;password=$State.password;userId=$registered.userId;teamId=$team.id})
        if (($index+1)%50 -eq 0 -or $index+1 -eq $Vus) { Write-Host "准备选手：$($index+1)/$Vus" }
    }
    $current = Invoke-PerfHttp $State GET "/api/v1/admin/competitions/$competitionId" -Token ADMIN
    $metadata = $current.competition
    $metadata.startTime = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')
    $null = Invoke-PerfHttp $State PATCH "/api/v1/admin/competitions/$competitionId" @{metadata=$metadata} ADMIN
    $null = Invoke-PerfHttp $State PUT "/api/v1/admin/competitions/$competitionId/status" @{status='Published'} ADMIN 204
    # 生命周期调度器也可能已经完成启动；状态转换入口本身保证幂等。
    $null = Invoke-PerfHttp $State PUT "/api/v1/admin/competitions/$competitionId/status" @{status='Running'} ADMIN 204
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $response = Invoke-WebRequest "$($State.baseUrl)/api/v1/competitions/$competitionId/leaderboard" -SkipHttpErrorCheck
        if ($response.StatusCode -eq 200) { break }
        if ($response.StatusCode -ne 202) { throw "排行榜准备失败：$($response.StatusCode)" }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($response.StatusCode -ne 200) { throw '排行榜准备超时。' }
    $data = @{runId=$State.runId;baseUrl=$State.baseUrl;competitionId=$competitionId;challenges=$challenges;users=$users.ToArray()}
    Save-PerfJson $data (Join-Path $State.local 'data.json')
    $State.competitionId = $competitionId
    Save-PerfJson $State $State.stateFile
    return $data
}
