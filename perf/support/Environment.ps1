Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Save-PerfJson($Value, [string]$Path) {
    $Value | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-PerfNative([string]$Executable, [string[]]$Arguments, [string]$Log) {
    & $Executable @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { throw "$Executable 执行失败，退出码 $LASTEXITCODE。诊断文件：$Log" }
}

function Invoke-PerfCompose($State, [string[]]$Arguments) {
    & docker compose --project-name $State.project --env-file $State.envFile -f $State.compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose 执行失败。' }
}

function Get-PerfPort($State, [string]$Service, [int]$Port) {
    $mapping = Invoke-PerfCompose $State @('port', $Service, "$Port")
    return [int]([uri]"http://$mapping").Port
}

function Invoke-PerfHttp {
    param($State, [string]$Method, [string]$Path, $Body = $null,
        [string]$Token = '', [int]$Expected = 200, $Form = $null)
    if ($Token -eq 'ADMIN') {
        if (!$State.adminToken -or [DateTimeOffset]::UtcNow.AddSeconds(60) -ge [DateTimeOffset]$State.adminExpires) {
            $login = Invoke-PerfHttp $State POST '/api/v1/auth/login' @{login=$State.adminUser;password=$State.password}
            if ($login.state -ne 'Authenticated') { throw '管理员认证未完成。' }
            $State.adminToken = $login.accessToken
            $State.adminExpires = $login.expiresAt
        }
        $Token = $State.adminToken
    }
    if ($Path -in @('/api/v1/auth/login', '/api/v1/auth/register')) {
        Start-Sleep -Milliseconds 260
    }
    $parameters = @{
        Uri = $State.baseUrl + $Path; Method = $Method; TimeoutSec = 45
        SkipHttpErrorCheck = $true; Headers = @{'Idempotency-Key'=[guid]::NewGuid().ToString()}
    }
    if ($Token) { $parameters.Headers.Authorization = "Bearer $Token" }
    if ($null -ne $Form) { $parameters.Form = $Form }
    elseif ($null -ne $Body) { $parameters.Body = ConvertTo-Json $Body -Depth 40 -Compress; $parameters.ContentType = 'application/json' }
    $response = Invoke-WebRequest @parameters
    if ([int]$response.StatusCode -ne $Expected) {
        # 错误正文可能含有受保护内容，仅保留稳定错误代码。
        $code = ''
        if ($response.Headers.'Content-Type' -match 'json') {
            $errorBody = $response.Content | ConvertFrom-Json -AsHashtable
            if ($errorBody.ContainsKey('code')) { $code = $errorBody.code }
            elseif ($errorBody.ContainsKey('errors')) { $code = ConvertTo-Json $errorBody.errors -Compress }
        }
        throw "$Method $Path 返回 $($response.StatusCode)，预期 $Expected。$code"
    }
    if ($response.Content -and $response.Headers.'Content-Type' -match 'json') {
        return $response.Content | ConvertFrom-Json -AsHashtable
    }
}

function Start-PerfEnvironment([string]$PerfRoot, [string]$RunId, [string]$ReportDirectory, [bool]$Observability=$true, [bool]$ServeFrontend=$false, [int]$AccessTokenMinutes=15, [string]$ApplicationSnapshot='', [bool]$CaptureDatabasePlanning=$false) {
    foreach ($command in @('docker', 'dotnet', 'k6')) { $null = Get-Command $command -ErrorAction Stop }
    Invoke-PerfNative docker @('info', '--format', '{{.ServerVersion}}') (Join-Path $ReportDirectory 'docker-version.log')
    $local = Join-Path $PerfRoot ".local/$RunId"
    $null = New-Item -ItemType Directory -Path $local -Force
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $state = @{
        runId=$RunId; project="noctf-perf-$RunId"; compose=(Join-Path $PerfRoot 'compose.yml')
        envFile=(Join-Path $local 'compose.env'); local=$local; reports=$ReportDirectory
        password=$password; adminUser="perf-admin-$RunId"; adminToken=''; adminExpires='1970-01-01'
        stateFile=(Join-Path $local 'state.json'); baseUrl=''
    }
    @("PERF_PASSWORD=$password", "PERF_QUERY_PLANNING=$(if($CaptureDatabasePlanning){'on'}else{'off'})") | Set-Content -LiteralPath $state.envFile -Encoding utf8
    Save-PerfJson $state $state.stateFile
    Invoke-PerfCompose $state @('config', '--quiet')
    Invoke-PerfCompose $state @('up', '-d', '--wait', '--wait-timeout', '180')
    $state.containers=@{}
    foreach ($service in @('postgres','redis','nats','storage')) { $state.containers[$service]=[string](Invoke-PerfCompose $state @('ps','-q',$service)) }
    Get-Content (Join-Path $PerfRoot 'support/sql/initialize.sql') -Raw | & docker exec -i $state.containers.postgres psql -X -U noctf -d noctf -v ON_ERROR_STOP=1 *> (Join-Path $ReportDirectory 'postgres-initialize.log')
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL 诊断扩展初始化失败。' }
    $postgresSettings=Get-Content (Join-Path $PerfRoot 'support/sql/settings.sql') -Raw | & docker exec -i $state.containers.postgres psql -X -q -U noctf -d noctf -A -t -v ON_ERROR_STOP=1
    if($LASTEXITCODE -ne 0){throw 'PostgreSQL 参数读取失败。'}
    Save-PerfJson ($postgresSettings | ConvertFrom-Json -AsHashtable) (Join-Path $ReportDirectory 'diagnostics/postgres-settings.json')
    $postgres = Get-PerfPort $state postgres 5432
    $redis = Get-PerfPort $state redis 6379
    $nats = Get-PerfPort $state nats 4222
    $storage = Get-PerfPort $state storage 9000
    $natsMonitor = Get-PerfPort $state nats 8222
    $state.natsMonitorUrl="http://127.0.0.1:$natsMonitor"
    $repository = Split-Path $PerfRoot
    $hostDirectory = Join-Path $repository 'backend/src/NoCTF.Host'
    $project = Join-Path $hostDirectory 'NoCTF.Host.csproj'
    if($ApplicationSnapshot){$buildDirectory=$ApplicationSnapshot}
    else {
        Invoke-PerfNative dotnet @('build', $project, '-c', 'Release', '--nologo') (Join-Path $ReportDirectory 'build.log')
        $buildDirectory=Join-Path $hostDirectory 'bin/Release/net10.0'
    }
    $isolatedHost=Join-Path $local 'application/NoCTF.Host'
    $isolatedApi=Join-Path $local 'application/NoCTF.API'
    $null=New-Item -ItemType Directory -Path $isolatedHost,$isolatedApi -Force
    Get-ChildItem -LiteralPath $buildDirectory -Force | Copy-Item -Destination $isolatedHost -Recurse
    Get-ChildItem (Join-Path $repository 'backend/src/NoCTF.API') -Filter 'appsettings*.json' | Copy-Item -Destination $isolatedApi
    if($ServeFrontend){
        $frontend=Join-Path $repository 'backend/src/NoCTF.API/ClientApp'
        Push-Location $frontend
        try {
            Invoke-PerfNative bun @('install','--frozen-lockfile') (Join-Path $ReportDirectory 'frontend-install.log')
            Invoke-PerfNative bun @('run','generate') (Join-Path $ReportDirectory 'frontend-build.log')
        } finally { Pop-Location }
        $webRoot=Join-Path $isolatedHost 'wwwroot'
        $null=New-Item -ItemType Directory -Path $webRoot -Force
        Get-ChildItem (Join-Path $frontend '.output/public') -Force | Copy-Item -Destination $webRoot -Recurse -Force
    }
    $hostDirectory=$isolatedHost
    $state.binaryDirectory=$isolatedHost
    Save-PerfJson @{assemblies=@(Get-ChildItem $isolatedHost -Filter 'NoCTF.*.dll' -File | ForEach-Object {
        @{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    });applicationConnectionBudget=80;postgresConnectionLimit=100;maximumAutoPreparedStatements=64;autoPrepareMinUsages=2} (Join-Path $ReportDirectory 'application.json')
    $dll = Join-Path $isolatedHost 'NoCTF.Host.dll'
    if (!(Test-Path -LiteralPath $dll)) { throw '未找到 Release Host 程序。' }
    # 通过系统分配本机端口，启动失败立即报告。
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $metricsListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $metricsListener.Start()
    $metricsPort = $metricsListener.LocalEndpoint.Port
    $listener.Stop()
    $metricsListener.Stop()
    $state.baseUrl = "http://127.0.0.1:$port"
    $state.metricsUrl="http://127.0.0.1:$metricsPort/metrics"
    $state.observability=$Observability
    $settings = @{
        ASPNETCORE_ENVIRONMENT='Production'; ASPNETCORE_URLS="$($state.baseUrl);http://127.0.0.1:$metricsPort"
        Hosting__Roles__0='Api'; Hosting__Roles__1='Worker'
        Database__MaximumPoolSize='80'
        ConnectionStrings__PostgreSql="Host=127.0.0.1;Port=$postgres;Database=noctf;Username=noctf;Password=$password;Maximum Pool Size=80;Max Auto Prepare=64;Auto Prepare Min Usages=2"
        ConnectionStrings__Redis="127.0.0.1:$redis"; ConnectionStrings__Nats="nats://127.0.0.1:$nats"
        Authentication__SigningKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
        Authentication__AccessTokenMinutes="$AccessTokenMinutes"
        Authentication__RefreshCookieSecure='false'; Authentication__RefreshAllowedOrigins__0=$state.baseUrl
        EmailVerification__EncryptionKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        EmailVerification__Enabled='false'; HumanVerification__Provider='None'
        SeedAdmin__UserName=$state.adminUser; SeedAdmin__Email="$($state.adminUser)@perf.test"; SeedAdmin__Password=$password
        RunnerScoring__SigningKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
        RunnerScoring__CallbackBaseUrl=$state.baseUrl; Runtime__Provider='Docker'; Runtime__RunnerPool='perf'
        Storage__Provider='S3'; Storage__S3__ServiceUrl="http://127.0.0.1:$storage"; Storage__S3__Bucket='noctf'
        Storage__S3__Region='us-east-1'; Storage__S3__ForcePathStyle='true'; Storage__S3__AccessKey='noctfperf'; Storage__S3__SecretKey=$password
        Observability__Enabled=$Observability.ToString(); Observability__MetricsPort="$metricsPort"
        OTEL_EXPORTER_OTLP_ENDPOINT=''; Observability__LokiBaseUrl=''; Logging__LogLevel__Default='Warning'
    }
    $state.settings = $settings
    Save-PerfJson $state $state.stateFile
    $saved = @{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        Push-Location $hostDirectory
        try {
            Invoke-PerfNative dotnet @($dll, '--initialize-storage-only') (Join-Path $ReportDirectory 'storage.log')
            Invoke-PerfNative dotnet @($dll, '--migrate-only') (Join-Path $ReportDirectory 'migration.log')
            $process = Start-Process dotnet -ArgumentList @('"'+$dll+'"') -WorkingDirectory $hostDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $ReportDirectory 'host.log') -RedirectStandardError (Join-Path $ReportDirectory 'host-error.log')
        } finally { Pop-Location }
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    $state.hostPid = $process.Id
    $state.hostStartTime = $process.StartTime.ToUniversalTime().ToString('O')
    Save-PerfJson $state $state.stateFile
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "Host 已退出。诊断目录：$ReportDirectory" }
        $response = $null
        try { $response = Invoke-WebRequest ($state.baseUrl+'/health') -SkipHttpErrorCheck -TimeoutSec 5 }
        catch [Microsoft.PowerShell.Commands.HttpResponseException] { throw }
        catch [System.Net.Http.HttpRequestException] { if ($process.HasExited) { throw 'Host 在就绪前退出。' } }
        if ($null -ne $response -and $response.StatusCode -eq 200) { return $state }
        Start-Sleep -Seconds 1
    }
    throw "Host 健康检查超时。诊断目录：$ReportDirectory"
}
