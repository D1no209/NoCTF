[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Context,
    [int]$TargetPods=1000,
    [int]$ServiceCpuMillicores=500,
    [int]$ServiceMemoryMiB=512,
    [int]$PublicEntriesPerService=1,
    [double]$Headroom=.2,
    [int]$NodePortMinimum=30000,
    [int]$NodePortMaximum=32767,
    [string]$OutputPath
)
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
if ($TargetPods -lt 1 -or $ServiceCpuMillicores -lt 1 -or $ServiceMemoryMiB -lt 1 -or $PublicEntriesPerService -lt 0 -or $Headroom -lt 0 -or $Headroom -ge 1) { throw 'Invalid capacity planning inputs.' }
if ($Context -eq 'kind-noctf-dev') { $NodePortMaximum=30127 }
if ($NodePortMaximum -lt $NodePortMinimum) { throw 'Invalid NodePort range.' }
if (-not $OutputPath) { $OutputPath=Join-Path $script:StateRoot 'capacity-budget.json' }
function Cpu-Millicores([string]$Value) {
    if ($Value.EndsWith('m')) { return [double]$Value.TrimEnd('m') }
    if ($Value.EndsWith('n')) { return [double]$Value.TrimEnd('n') / 1000000 }
    if ($Value.EndsWith('u')) { return [double]$Value.TrimEnd('u') / 1000 }
    return [double]$Value * 1000
}
function Memory-MiB([string]$Value) {
    if ($Value -match '^([0-9.]+)([KMGT]i?)?$') {
        $number=[double]$Matches[1]
        switch ($Matches[2]) {
            'Ki' { return $number / 1024 }
            'Mi' { return $number }
            'Gi' { return $number * 1024 }
            'Ti' { return $number * 1048576 }
            'K' { return $number * 1000 / 1MB }
            'M' { return $number * 1000000 / 1MB }
            'G' { return $number * 1000000000 / 1MB }
            'T' { return $number * 1000000000000 / 1MB }
            default { return $number / 1MB }
        }
    }
    throw 'Unsupported Kubernetes memory quantity.'
}
function Container-Request($Container,[string]$Resource) {
    if (-not $Container.PSObject.Properties['resources'] -or -not $Container.resources -or -not $Container.resources.PSObject.Properties['requests'] -or -not $Container.resources.requests -or -not $Container.resources.requests.PSObject.Properties[$Resource]) { return 0 }
    if ($Resource -eq 'cpu') { return Cpu-Millicores $Container.resources.requests.cpu }
    return Memory-MiB $Container.resources.requests.memory
}
$nodes=Get-KubeJson @('get','nodes','-l','noctf.io/pod-pids-limit=256')
$pods=Get-KubeJson @('get','pods','--all-namespaces')
$services=Get-KubeJson @('get','services','--all-namespaces')
$rows=@()
foreach ($node in $nodes.items) {
    if ($node.spec.PSObject.Properties.Name -contains 'unschedulable' -and $node.spec.unschedulable) { continue }
    if (-not ($node.status.conditions | Where-Object { $_.type -eq 'Ready' -and $_.status -eq 'True' })) { continue }
    $active=@($pods.items | Where-Object { $_.spec.PSObject.Properties.Name -contains 'nodeName' -and $_.spec.nodeName -eq $node.metadata.name -and $_.status.phase -notin @('Succeeded','Failed') })
    $usedCpu=0; $usedMemory=0
    foreach ($pod in $active) {
        $cpu=0; $memory=0
        foreach ($container in $pod.spec.containers) { $cpu+=Container-Request $container cpu; $memory+=Container-Request $container memory }
        if ($pod.spec.PSObject.Properties.Name -contains 'initContainers') {
            foreach ($container in $pod.spec.initContainers) {
                # Conservatively count restartable init sidecars alongside the application.
                if ($container.PSObject.Properties.Name -contains 'restartPolicy' -and $container.restartPolicy -eq 'Always') { $cpu+=Container-Request $container cpu; $memory+=Container-Request $container memory }
                else { $cpu=[Math]::Max($cpu,(Container-Request $container cpu)); $memory=[Math]::Max($memory,(Container-Request $container memory)) }
            }
        }
        $usedCpu+=$cpu; $usedMemory+=$memory
    }
    $rows+=@{node=$node.metadata.name;cpuMillicores=[Math]::Max(0,(Cpu-Millicores $node.status.allocatable.cpu)-$usedCpu);memoryMiB=[Math]::Max(0,(Memory-MiB $node.status.allocatable.memory)-$usedMemory);podSlots=[Math]::Max(0,[int]$node.status.allocatable.pods-$active.Count)}
}
$ports=@($services.items | ForEach-Object { $_.spec.ports } | Where-Object { $_.PSObject.Properties.Name -contains 'nodePort' -and $_.nodePort -ge $NodePortMinimum -and $_.nodePort -le $NodePortMaximum } | ForEach-Object { $_.nodePort } | Sort-Object -Unique)
$availablePorts=$NodePortMaximum-$NodePortMinimum+1-$ports.Count
$limits=@{
    cpuPods=[int][Math]::Floor((($rows | ForEach-Object { $_.cpuMillicores } | Measure-Object -Sum).Sum)*(1-$Headroom)/$ServiceCpuMillicores)
    memoryPods=[int][Math]::Floor((($rows | ForEach-Object { $_.memoryMiB } | Measure-Object -Sum).Sum)*(1-$Headroom)/$ServiceMemoryMiB)
    slotPods=[int][Math]::Floor((($rows | ForEach-Object { $_.podSlots } | Measure-Object -Sum).Sum)*(1-$Headroom))
    publicPods=if ($PublicEntriesPerService -eq 0) { $null } else { [int][Math]::Floor($availablePorts*(1-$Headroom)/$PublicEntriesPerService) }
}
$quotas=Get-KubeJson @('-n','runtime','get','resourcequota')
$quotaRows=@()
foreach ($quota in $quotas.items) {
    $row=@{name=$quota.metadata.name}
    foreach ($resource in @('requests.cpu','requests.memory','pods')) {
        if (-not $quota.status.hard.PSObject.Properties[$resource]) { continue }
        $hard=$quota.status.hard.PSObject.Properties[$resource].Value
        $used=if ($quota.status.used.PSObject.Properties[$resource]) { $quota.status.used.PSObject.Properties[$resource].Value } else { '0' }
        switch ($resource) {
            'requests.cpu' { $remaining=[Math]::Max(0,(Cpu-Millicores $hard)-(Cpu-Millicores $used)); $limit='cpuPods'; $unit=$ServiceCpuMillicores }
            'requests.memory' { $remaining=[Math]::Max(0,(Memory-MiB $hard)-(Memory-MiB $used)); $limit='memoryPods'; $unit=$ServiceMemoryMiB }
            'pods' { $remaining=[Math]::Max(0,[int]$hard-[int]$used); $limit='slotPods'; $unit=1 }
        }
        $row[$resource]=$remaining
        $limits[$limit]=[int][Math]::Min($limits[$limit],[Math]::Floor($remaining*(1-$Headroom)/$unit))
    }
    $quotaRows+=$row
}
$supported=($limits.Values | Where-Object { $null -ne $_ } | Measure-Object -Minimum).Minimum
$report=@{context=$Context;observedAt=[DateTimeOffset]::UtcNow.ToString('o');targetPods=$TargetPods;planningCpuMillicores=$ServiceCpuMillicores;planningMemoryMiB=$ServiceMemoryMiB;publicEntriesPerService=$PublicEntriesPerService;headroom=$Headroom;eligibleNodes=$rows;nodePortRange=@($NodePortMinimum,$NodePortMaximum);usedNodePorts=$ports.Count;availableNodePorts=$availablePorts;limits=$limits;planningUpperBound=[int]$supported;targetFits=($supported -ge $TargetPods);scope='Request/slot/port planning upper bound, not a load-test result. Account for quotas, actual utilization and per-node packing before admission.'}
$report['remainingNamespaceQuotas']=$quotaRows
Save-Json $OutputPath $report
Write-Host "Capacity report: $OutputPath; planning upper bound=$supported; target=$TargetPods"
