param(
    [switch]$KeepEnvironment
)

$ErrorActionPreference = "Stop"

function Invoke-DockerCompose {
    param([Parameter(ValueFromRemainingArguments)] [string[]]$Arguments)
    & docker compose -p $script:ProjectName -f $script:ComposeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Convert-BuildProxy([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return $Value `
        -replace "127\.0\.0\.1", "host.docker.internal" `
        -replace "localhost", "host.docker.internal"
}

function Resolve-E2EApiPort {
    $portOutput = @(& docker compose -p $script:ProjectName -f $script:ComposeFile `
        port backend 8080 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Resolving the Docker-assigned API port failed: $(($portOutput | Out-String).Trim())"
    }
    $publishedEndpoint = ($portOutput | Out-String).Trim()
    if ($publishedEndpoint -notmatch ':(?<port>[0-9]+)$') {
        throw "Docker returned an invalid API port mapping: '$publishedEndpoint'."
    }
    return [int]$Matches.port
}

function Remove-E2EResources {
    $containerIds = [System.Collections.Generic.HashSet[string]]::new()
    $networkNames = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($id in @(docker ps -aq --filter "ancestor=$script:RuntimeImage")) {
        if (-not [string]::IsNullOrWhiteSpace($id)) { [void]$containerIds.Add($id.Trim()) }
    }
    $platformNetworkId = docker network ls -q --filter "name=^$([regex]::Escape($script:NetworkName))$"
    if (-not [string]::IsNullOrWhiteSpace($platformNetworkId)) {
        $attached = docker network inspect $script:NetworkName `
            --format '{{range $id, $container := .Containers}}{{$id}} {{end}}'
        foreach ($id in ($attached -split '\s+')) {
            if (-not [string]::IsNullOrWhiteSpace($id)) { [void]$containerIds.Add($id.Trim()) }
        }
    }

    foreach ($id in $containerIds) {
        $networks = docker inspect $id --format '{{range $name, $network := .NetworkSettings.Networks}}{{$name}} {{end}}' 2>$null
        foreach ($name in ($networks -split '\s+')) {
            if (-not [string]::IsNullOrWhiteSpace($name) -and
                $name -notin @("bridge", "host", "none")) {
                [void]$networkNames.Add($name.Trim())
            }
        }
    }
    if ($containerIds.Count -gt 0) {
        & docker rm -f @($containerIds)
    }

    try { Invoke-DockerCompose -Arguments @("down", "-v", "--remove-orphans", "--rmi", "local") }
    catch { Write-Warning $_ }

    foreach ($name in $networkNames) {
        $resolved = docker network ls -q --filter "name=^$([regex]::Escape($name))$"
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            & docker network rm $name | Out-Null
        }
    }
    $fixtureImage = docker image ls -q $script:RuntimeImage
    if (-not [string]::IsNullOrWhiteSpace($fixtureImage)) {
        & docker image rm $script:RuntimeImage | Out-Null
    }
}

& docker version | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Docker is required for the CTF E2E test." }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$script:ComposeFile = Join-Path $repositoryRoot "backend\tests\NoCTF.E2E\docker-compose.ctf.yml"
$suffix = "{0}-{1}" -f $PID, [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:ProjectName = "noctf-ctf-e2e-$suffix".ToLowerInvariant()
$script:NetworkName = "$($script:ProjectName)-network"
$script:RuntimeImage = "$($script:ProjectName)-runtime:latest"

$env:NOCTF_E2E_NETWORK = $script:NetworkName
$env:NOCTF_E2E_RUNTIME_IMAGE = $script:RuntimeImage
$env:NOCTF_E2E_POSTGRES_PASSWORD = "ctf-e2e-postgres-$suffix"
$env:NOCTF_E2E_MINIO_USER = "ctfe2e"
$env:NOCTF_E2E_MINIO_PASSWORD = "ctf-e2e-minio-$suffix"
$env:NOCTF_E2E_JWT_SECRET = "ctf-e2e-jwt-signing-key-$suffix-0123456789"
$env:NOCTF_E2E_ADMIN_PASSWORD = "ctf-e2e-admin-$suffix"
$env:NOCTF_E2E_BUILD_HTTP_PROXY = Convert-BuildProxy $env:HTTP_PROXY
$env:NOCTF_E2E_BUILD_HTTPS_PROXY = Convert-BuildProxy $env:HTTPS_PROXY
$env:NO_PROXY = "127.0.0.1,localhost"
$env:no_proxy = $env:NO_PROXY

$succeeded = $false
try {
    Invoke-DockerCompose -Arguments @("build", "--quiet", "runtime-fixture", "migration", "backend", "worker", "runner")
    Invoke-DockerCompose -Arguments @("up", "-d", "postgres", "redis", "minio", "minio-init", "migration", "backend", "worker", "runner")
    $apiPort = Resolve-E2EApiPort
    $env:NOCTF_E2E_BASE_URL = "http://127.0.0.1:$apiPort"

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
    $ready = $false
    $lastApiStatus = "not requested"
    $lastRedisHeartbeat = "not requested"
    $httpHandler = [System.Net.Http.HttpClientHandler]::new()
    $httpHandler.UseProxy = $false
    $httpClient = [System.Net.Http.HttpClient]::new($httpHandler)
    $httpClient.Timeout = [TimeSpan]::FromSeconds(3)
    try {
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            try {
                $response = $httpClient.GetAsync("$($env:NOCTF_E2E_BASE_URL)/health").GetAwaiter().GetResult()
                $lastApiStatus = [int]$response.StatusCode
                $response.Dispose()
            }
            catch {
                $lastApiStatus = "error: $($_.Exception.Message)"
            }

            $heartbeat = @(docker compose -p $script:ProjectName -f $script:ComposeFile `
                exec -T redis redis-cli EXISTS runner:ctf-e2e-runner-1:heartbeat 2>&1)
            $lastRedisHeartbeat = ($heartbeat | Out-String).Trim()
            if ($lastApiStatus -eq 200 -and $LASTEXITCODE -eq 0 -and $lastRedisHeartbeat -eq "1") {
                $ready = $true
                break
            }

            Start-Sleep -Seconds 1
        }
    }
    finally {
        $httpClient.Dispose()
        $httpHandler.Dispose()
    }
    if (-not $ready) {
        throw "The isolated API and Runner did not become ready. Last API status: $lastApiStatus. Last Redis heartbeat output: $lastRedisHeartbeat"
    }

    $backendPath = Join-Path $repositoryRoot "backend"
    Push-Location $backendPath
    try {
        & dotnet run --project tests/NoCTF.E2E/NoCTF.E2E.csproj -- `
            --treenode-filter '/*/*/*/*[Category=CtfE2E]' --minimum-expected-tests 1
    }
    finally {
        Pop-Location
    }
    if ($LASTEXITCODE -ne 0) { throw "The CTF E2E test failed with exit code $LASTEXITCODE." }
    $succeeded = $true
}
finally {
    if (-not $succeeded) {
        try { Invoke-DockerCompose -Arguments @("ps", "-a") } catch { Write-Warning $_ }
        try { Invoke-DockerCompose -Arguments @("logs", "--no-color", "--tail", "200", "backend", "worker", "runner") } catch { Write-Warning $_ }
    }
    if ($KeepEnvironment) {
        Write-Host "CTF E2E environment retained: project=$($script:ProjectName) api=$($env:NOCTF_E2E_BASE_URL)"
    }
    else {
        Remove-E2EResources
    }
}
