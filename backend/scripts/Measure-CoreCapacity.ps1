param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/capacity-measurements'),
    [switch]$HostPressure,
    [string]$RuntimeImage = 'mcr.microsoft.com/dotnet/aspnet:10.0'
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path $PSScriptRoot -Parent
$previousMeasurements = $env:NOCTF_CAPACITY_MEASUREMENTS
Push-Location $backendRoot
try {
    $destination = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    git rev-parse HEAD | Set-Content (Join-Path $destination 'commit.txt')
    git status --short | Set-Content (Join-Path $destination 'working-tree.txt')
    dotnet --info | Set-Content (Join-Path $destination 'dotnet-info.txt')
    docker version | Set-Content (Join-Path $destination 'docker-version.txt')
    dotnet build NoCTF.slnx -c Release --no-restore -m:1 -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $env:NOCTF_CAPACITY_MEASUREMENTS = $destination
    dotnet tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/PersistedRunnerCapacityGateTests/*' --minimum-expected-tests 2
    if ($LASTEXITCODE -ne 0) { throw 'Capacity load verification failed.' }
    if ($HostPressure) {
        # Docker-host bind sources intentionally refer to the daemon's Linux filesystem.
        $assemblyDirectory = Join-Path $backendRoot 'tests/NoCTF.Tests/bin/Release/net10.0'
        docker run --rm --user 0 --entrypoint dotnet `
            --mount "type=bind,src=$assemblyDirectory,dst=/tests" `
            --mount "type=bind,src=$destination,dst=/measurements" `
            --mount type=bind,src=/var/run/docker.sock,dst=/var/run/docker.sock `
            --mount type=bind,src=/proc,dst=/host/proc,readonly `
            --mount type=bind,src=/sys/fs/cgroup,dst=/host/sys/fs/cgroup,readonly `
            --mount type=bind,src=/etc/machine-id,dst=/host/etc/machine-id,readonly `
            --add-host host.docker.internal:host-gateway `
            -e NOCTF_HOST_PRESSURE_TEST=true -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
            -e NOCTF_CAPACITY_MEASUREMENTS=/measurements -e DOCKER_HOST=unix:///var/run/docker.sock `
            $RuntimeImage /tests/NoCTF.Tests.dll --treenode-filter '/*/*/DockerHostPressureTests/*' --minimum-expected-tests 1
        if ($LASTEXITCODE -ne 0) { throw 'Docker daemon-host pressure verification failed.' }
    }
    dotnet tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/HistoricalAdjudicationPreviewPersistenceTests/Preview_uses_one_repeatable_snapshot_and_a_bounded_query_count' --minimum-expected-tests 1
    if ($LASTEXITCODE -ne 0) { throw 'Preview scan measurement failed.' }
} finally {
    $env:NOCTF_CAPACITY_MEASUREMENTS = $previousMeasurements
    Pop-Location
}
