param(
    [switch]$KeepEnvironment
)

$ErrorActionPreference = "Stop"

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

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

function Remove-E2EResources {
    $containerIds = [System.Collections.Generic.HashSet[string]]::new()
    $networkNames = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($image in @($script:RuntimeImage, $script:CheckerImage)) {
        foreach ($id in @(docker ps -aq --filter "ancestor=$image")) {
            if (-not [string]::IsNullOrWhiteSpace($id)) {
                [void]$containerIds.Add($id.Trim())
            }
        }
    }
    $platformNetworkId = docker network ls -q --filter "name=^$([regex]::Escape($script:NetworkName))$"
    if (-not [string]::IsNullOrWhiteSpace($platformNetworkId)) {
        $attached = docker network inspect $script:NetworkName `
            --format '{{range $id, $container := .Containers}}{{$id}} {{end}}'
        foreach ($id in ($attached -split '\s+')) {
            if (-not [string]::IsNullOrWhiteSpace($id)) {
                [void]$containerIds.Add($id.Trim())
            }
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
        & docker rm -f @($containerIds) | Out-Null
    }

    try { Invoke-DockerCompose -Arguments @("down", "-v", "--remove-orphans", "--rmi", "local") }
    catch { Write-Warning $_ }

    foreach ($name in $networkNames) {
        $resolved = docker network ls -q --filter "name=^$([regex]::Escape($name))$"
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            & docker network rm $name | Out-Null
        }
    }
    foreach ($image in @($script:RuntimeImage, $script:CheckerImage)) {
        $fixtureImage = docker image ls -q $image
        if (-not [string]::IsNullOrWhiteSpace($fixtureImage)) {
            & docker image rm $image | Out-Null
        }
    }
}

& docker version | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Docker is required for the AWD E2E test." }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$script:ComposeFile = Join-Path $repositoryRoot "backend\tests\NoCTF.E2E\docker-compose.awd.yml"
$suffix = "{0}-{1}" -f $PID, [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:ProjectName = "noctf-awd-e2e-$suffix".ToLowerInvariant()
$script:NetworkName = "$($script:ProjectName)-network"
$script:RuntimeImage = "$($script:ProjectName)-runtime:latest"
$script:CheckerImage = "$($script:ProjectName)-checker:latest"
$callbackContainer = "$($script:ProjectName)-backend"
$apiPort = Get-FreeTcpPort

$env:NOCTF_E2E_API_PORT = $apiPort.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:NOCTF_E2E_NETWORK = $script:NetworkName
$env:NOCTF_E2E_RUNTIME_IMAGE = $script:RuntimeImage
$env:NOCTF_E2E_CHECKER_IMAGE = $script:CheckerImage
$env:NOCTF_E2E_CALLBACK_CONTAINER = $callbackContainer
$env:NOCTF_E2E_POSTGRES_PASSWORD = "awd-e2e-postgres-$suffix"
$env:NOCTF_E2E_MINIO_USER = "awde2e"
$env:NOCTF_E2E_MINIO_PASSWORD = "awd-e2e-minio-$suffix"
$env:NOCTF_E2E_JWT_SECRET = "awd-e2e-jwt-signing-key-$suffix-0123456789"
$env:NOCTF_E2E_ADMIN_PASSWORD = "awd-e2e-admin-$suffix"
$env:NOCTF_E2E_BUILD_HTTP_PROXY = Convert-BuildProxy $env:HTTP_PROXY
$env:NOCTF_E2E_BUILD_HTTPS_PROXY = Convert-BuildProxy $env:HTTPS_PROXY
$env:NOCTF_E2E_BASE_URL = "http://127.0.0.1:$apiPort"

$succeeded = $false
try {
    Invoke-DockerCompose -Arguments @(
        "build", "--quiet", "runtime-fixture", "checker-fixture",
        "migration", "backend", "worker", "runner")
    Invoke-DockerCompose -Arguments @(
        "up", "-d", "postgres", "redis", "minio", "minio-init",
        "migration", "backend", "worker", "runner")

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
    $ready = $false
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "$($env:NOCTF_E2E_BASE_URL)/health" -TimeoutSec 3
            if ($response.StatusCode -eq 200) {
                $heartbeat = docker compose -p $script:ProjectName -f $script:ComposeFile `
                    exec -T redis redis-cli EXISTS runner:awd-e2e-runner-1:heartbeat
                if ($heartbeat -eq "1") {
                    $ready = $true
                    break
                }
            }
        }
        catch {
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw "The isolated API and Runner did not become ready." }

    $backendPath = Join-Path $repositoryRoot "backend"
    $backendWslPath = (& wsl -d Ubuntu-22.04 --exec wslpath -a -- $backendPath).Trim()
    $testCommand = "cd '$backendWslPath' && " +
        "env NOCTF_E2E_BASE_URL='$($env:NOCTF_E2E_BASE_URL)' " +
        "NOCTF_E2E_RUNTIME_IMAGE='$($env:NOCTF_E2E_RUNTIME_IMAGE)' " +
        "NOCTF_E2E_CHECKER_IMAGE='$($env:NOCTF_E2E_CHECKER_IMAGE)' " +
        "NOCTF_E2E_ADMIN_PASSWORD='$($env:NOCTF_E2E_ADMIN_PASSWORD)' " +
        "/home/developer/.dotnet/dotnet run --project tests/NoCTF.E2E/NoCTF.E2E.csproj --no-restore -- " +
        "--treenode-filter '/*/*/*/*[Category=AwdE2E]' --minimum-expected-tests 1"
    & wsl -d Ubuntu-22.04 -- bash -lc $testCommand
    if ($LASTEXITCODE -ne 0) { throw "The AWD E2E test failed with exit code $LASTEXITCODE." }
    $succeeded = $true
}
finally {
    if (-not $succeeded) {
        try { Invoke-DockerCompose -Arguments @("ps", "-a") } catch { Write-Warning $_ }
        try { Invoke-DockerCompose -Arguments @("logs", "--no-color", "--tail", "250", "backend", "worker", "runner") } catch { Write-Warning $_ }
    }
    if ($KeepEnvironment) {
        Write-Host "AWD E2E environment retained: project=$($script:ProjectName) api=$($env:NOCTF_E2E_BASE_URL)"
    }
    else {
        Remove-E2EResources
    }
}
