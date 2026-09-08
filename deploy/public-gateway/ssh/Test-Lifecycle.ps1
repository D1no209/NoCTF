param([string]$DockerHostAddress = 'host.docker.internal', [switch]$NonRoot,
    [string]$Filter = '/*/*/DockerSharedSshGatewayLifecycleTests/*')
$ErrorActionPreference = 'Stop'
# LOCAL disposable Docker fixture only. No SSH connection, deployment, production data or 1Panel access.
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
foreach ($image in @('noctf-gateway-ssh-prototype:local', 'noctf-gateway-ssh-client:local', 'noctf-gateway-relay:local')) {
    docker image inspect $image --format '{{.Id}}' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build local fixture image first: $image" }
}
dotnet build (Join-Path $taskRepo 'backend/tests/NoCTF.Tests') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
$fixtureId = [Guid]::NewGuid().ToString('N')
$fixtureVolume = "noctf-gateway-lifecycle-$fixtureId"
docker volume create --label noctf.io/isolated-gateway-test=true $fixtureVolume | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated Linux filesystem fixture.' }
try {
    $mountpoint = (docker volume inspect $fixtureVolume --format '{{.Mountpoint}}').Trim()
    if ($LASTEXITCODE -ne 0 -or !$mountpoint.StartsWith('/')) { throw 'Could not resolve fixture host path.' }
    $processUser = '0:0'
    if ($NonRoot) {
        # Standard primary Docker socket group, no extra groups or permission changes to the socket.
        $socketGroup = (docker run --rm --mount 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock' `
            mcr.microsoft.com/dotnet/sdk@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 `
            stat -c '%g' /var/run/docker.sock).Trim()
        if ($LASTEXITCODE -ne 0 -or $socketGroup -notmatch '^[0-9]+$') { throw 'Cannot determine fixture Docker group.' }
        $processUser = "1654:$socketGroup"
        docker run --rm --mount "type=volume,source=$fixtureVolume,target=/state" `
            mcr.microsoft.com/dotnet/sdk@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 `
            chown $processUser /state
        if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the isolated non-root directory.' }
    }
    docker run --rm --name "noctf-gateway-lifecycle-$fixtureId" `
        --user $processUser `
        --mount "type=bind,source=$taskRepo/backend,target=/backend" `
        --mount 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock' `
        --mount "type=volume,source=$fixtureVolume,target=/state" `
        --env DOCKER_HOST=unix:///var/run/docker.sock `
        --env "TESTCONTAINERS_HOST_OVERRIDE=$DockerHostAddress" `
        --env NOCTF_REQUIRE_DOCKER_INTEGRATION=true `
        --env NOCTF_GATEWAY_LOCAL_STATE_ROOT=/state `
        --env "NOCTF_GATEWAY_HOST_STATE_ROOT=$mountpoint" `
        --workdir /backend `
        mcr.microsoft.com/dotnet/sdk@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 `
        dotnet /backend/tests/NoCTF.Tests/bin/Debug/net10.0/NoCTF.Tests.dll `
        --treenode-filter $Filter --output Detailed
    $result = $LASTEXITCODE
} finally {
    # Exact generated fixture only; Docker refuses removal if a failed test still mounts it.
    docker volume rm $fixtureVolume | Out-Null
}
if ($result -ne 0) { throw "Isolated Linux lifecycle test failed ($result)." }
