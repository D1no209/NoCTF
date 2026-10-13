[CmdletBinding()]
param(
    [ValidateSet('baseline', 'pressure')][string]$Scenario = 'baseline',
    [ValidateRange(1, 10)][int]$Runs = 1,
    [ValidateRange(1, 8)][int]$PressureParallelism = 4,
    [string]$Image = 'noctf-awd-load:diagnostics',
    [switch]$RemoveContainersAfterCapture,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Start-CapturedProcess([string]$Executable, [string[]]$Arguments, [string]$Name, [hashtable]$Environment = @{}) {
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory = $repository
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    foreach ($key in $Environment.Keys) { $info.Environment[$key] = $Environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $out = [IO.FileStream]::new("$Name.out.log", 'Create', 'Write', 'ReadWrite', 1, $true)
    $err = [IO.FileStream]::new("$Name.err.log", 'Create', 'Write', 'ReadWrite', 1, $true)
    $process.Start() | Out-Null
    return [pscustomobject]@{ Process = $process; Out = $out; Err = $err
        OutCopy = $process.StandardOutput.BaseStream.CopyToAsync($out)
        ErrCopy = $process.StandardError.BaseStream.CopyToAsync($err) }
}

function Close-CapturedProcess($Capture, [bool]$Stop = $false) {
    if ($null -eq $Capture) { return }
    if ($Stop -and -not $Capture.Process.HasExited) { $Capture.Process.Kill($true) }
    $Capture.Process.WaitForExit()
    $Capture.OutCopy.GetAwaiter().GetResult() | Out-Null
    $Capture.ErrCopy.GetAwaiter().GetResult() | Out-Null
    $Capture.Out.Dispose(); $Capture.Err.Dispose()
    $Capture.Process.Dispose()
}

for ($run = 1; $run -le $Runs; $run++) {
    $case = Join-Path $output "$Scenario-$run"
    if (Test-Path (Join-Path $case 'run.json')) { throw "Refusing to overwrite an existing run: $case" }
    New-Item -ItemType Directory -Force -Path $case | Out-Null
    $started = [DateTimeOffset]::UtcNow
    $pressure = $null; $e2e = $null; $events = $null
    $counters = @{}; $project = $null; $roles = @()
    $metadata = [ordered]@{ scenario = $Scenario; run = $run; startedAt = $started.ToString('O')
        commit = (git -C $repository rev-parse HEAD); image = $Image
        imageId = (docker image inspect $Image --format '{{.Id}}'); dirtyState = (git -C $repository status --porcelain)
        pressureParallelism = $(if ($Scenario -eq 'pressure') { $PressureParallelism } else { 0 })
        collector = 'Admin AWD evidence polling 1s; Docker stats; System.Runtime counters 1s for 120s; selected container lifecycle events'
        pressureFilter = '/*/NoCTF.Tests.Integration.Persistence/*/*'
        pressureBinarySha256 = (Get-FileHash (Join-Path $repository 'backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll') -Algorithm SHA256).Hash
        pressurePurpose = 'Concurrent load only; stopped when the measured E2E workload finishes, not used as a correctness result' }
    try {
        if ($Scenario -eq 'pressure') {
            $pressureArgs = @('backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll',
                '--treenode-filter', $metadata.pressureFilter, '--maximum-parallel-tests', "$PressureParallelism", '--minimum-expected-tests', '1')
            $pressure = Start-CapturedProcess 'dotnet' $pressureArgs (Join-Path $case 'pressure') @{ NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true' }
        }
        $e2eArgs = @('run', '--file', 'backend/tests/e2e.cs', '--', '--mode', 'awd', '--suite', 'smoke', '--keep-environment')
        $e2e = Start-CapturedProcess 'dotnet' $e2eArgs (Join-Path $case 'e2e') @{ NOCTF_E2E_HOST_IMAGE = $Image; NOCTF_E2E_DIAGNOSTICS_DIRECTORY = $case }
        $limit = $started.AddMinutes(12)
        while (-not $e2e.Process.HasExited) {
            if ([DateTimeOffset]::UtcNow -gt $limit) { throw 'Measured run exceeded the 12-minute guard.' }
            if ($null -eq $project) {
                $match = Select-String -Path (Join-Path $case 'e2e.out.log') -Pattern '^=== Awd \w+ E2E: (noctf-awd-e2e-[0-9]+-[0-9]+-[a-f0-9]+) ===' | Select-Object -First 1
                if ($match) {
                    $project = $match.Matches[0].Groups[1].Value
                    $metadata.project = $project
                    $metadata | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $case 'run.json') -Encoding utf8
                    $roles = @("$project-backend", "$project-worker-1", "$project-runner-1")
                    $format = '{"timeNano":{{.TimeNano}},"action":{{json .Action}},"id":{{json .Actor.ID}},"name":{{json (index .Actor.Attributes "name")}},"runtimeId":{{json (index .Actor.Attributes "noctf.io/runtime-instance-id")}}}'
                    $events = Start-CapturedProcess 'docker' @('events', '--since', $started.ToString('O'), '--filter', 'type=container',
                        '--filter', "label=com.docker.compose.project=$project", '--filter', 'event=create', '--filter', 'event=start',
                        '--filter', 'event=die', '--filter', 'event=kill', '--filter', 'event=destroy', '--format', $format) (Join-Path $case 'docker-events')
                    Write-Output "$Scenario/${run}: $project"
                }
            }
            if ($project) {
                $running = @(docker ps --filter "label=com.docker.compose.project=$project" --format '{{.Names}}')
                foreach ($role in $roles) {
                    if ($role -in $running -and -not $counters.ContainsKey($role)) {
                        $name = if ($role.EndsWith('-backend')) { 'api' } elseif ($role.EndsWith('-worker-1')) { 'worker' } else { 'runner' }
                        $counters[$role] = Start-CapturedProcess 'docker' @('exec', $role, '/out/tools/dotnet-counters', 'collect',
                            '--process-id', '1', '--counters', 'System.Runtime', '--refresh-interval', '1', '--format', 'csv',
                            '--duration', '00:00:02:00', '--output', "/tmp/$name-counters.csv") (Join-Path $case "$name-collector")
                        docker exec $role cat /sys/fs/cgroup/cpu.max /sys/fs/cgroup/cpu.stat /sys/fs/cgroup/memory.max /sys/fs/cgroup/memory.events `
                            > (Join-Path $case "$name-cgroup-start.txt")
                    }
                }
                $observed = @($roles | Where-Object { $_ -in $running })
                if ($observed.Count -gt 0) {
                    $stats = @(docker stats --no-stream --format json @observed | ForEach-Object { $_ | ConvertFrom-Json })
                    @{ capturedAt = [DateTimeOffset]::UtcNow.ToString('O'); stats = $stats } | ConvertTo-Json -Depth 5 -Compress |
                        Add-Content (Join-Path $case 'docker-stats.jsonl') -Encoding utf8
                }
            }
            Start-Sleep -Seconds 1
        }
        $metadata.completedAt = [DateTimeOffset]::UtcNow.ToString('O')
        $metadata.e2eExitCode = $e2e.Process.ExitCode
        if ($pressure) { $metadata.pressureRunningAtFinish = -not $pressure.Process.HasExited }
    }
    finally {
        Close-CapturedProcess $e2e $true
        Close-CapturedProcess $pressure $true
        Close-CapturedProcess $events $true
        foreach ($role in $counters.Keys) {
            $capture = $counters[$role]
            $capture.Process.WaitForExit(130000) | Out-Null
            Close-CapturedProcess $capture $true
            $name = if ($role.EndsWith('-backend')) { 'api' } elseif ($role.EndsWith('-worker-1')) { 'worker' } else { 'runner' }
            docker cp "${role}:/tmp/$name-counters.csv" (Join-Path $case "$name-counters.csv") 2> (Join-Path $case "$name-counter-copy.err.log")
            docker exec $role cat /sys/fs/cgroup/cpu.stat /sys/fs/cgroup/memory.events > (Join-Path $case "$name-cgroup-end.txt")
            docker logs --timestamps $role > (Join-Path $case "$name-host.log") 2>&1
        }
        $existingRoles = @(docker ps -a --format '{{.Names}}' | Where-Object { $_ -in $roles })
        if ($existingRoles.Count -gt 0) { docker stop @existingRoles > (Join-Path $case 'stopped-roles.txt') }
        if ($project) {
            $remaining = @(docker ps -q --filter "label=com.docker.compose.project=$project")
            if ($remaining.Count -gt 0) { docker stop @remaining > (Join-Path $case 'stopped-dependencies.txt') }
        }
        $metadata.retention = 'Project containers stopped; containers and volumes retained for investigation. Cleanup is explicit.'
        if ($RemoveContainersAfterCapture -and $project) {
            $owned = @(docker ps -aq --filter "label=com.docker.compose.project=$project")
            if ($owned.Count -gt 0) {
                $containers = docker inspect @owned | ConvertFrom-Json
                if (@($containers | Where-Object { $_.State.Running -or $_.Config.Labels.'com.docker.compose.project' -ne $project }).Count -gt 0) {
                    throw 'Refusing to remove active or unrelated containers.'
                }
                $containers | Select-Object Id, Name, @{n='volumes'; e={@($_.Mounts | Where-Object Type -eq volume | Select-Object Name, Destination)}} |
                    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $case 'retained-volumes.json') -Encoding utf8
                docker rm @owned | Out-Null
            }
            $networks = @(docker network ls -q --filter "label=com.docker.compose.project=$project")
            if ($networks.Count -gt 0) { docker network rm @networks | Out-Null }
            $metadata.retention = 'Fixture containers and project networks removed after capture; named data volumes retained in retained-volumes.json.'
        }
        $metadata | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $case 'run.json') -Encoding utf8
    }
    Write-Output "Result saved to $case"
}
