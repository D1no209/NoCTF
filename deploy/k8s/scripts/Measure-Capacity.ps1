[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Context,
    [Parameter(Mandatory)][string]$Image,
    [Parameter(Mandatory)][string]$PublicHost,
    [string[]]$Counts = @('50'),
    [string[]]$ImagePullSecrets = @(),
    [string]$ReportDirectory
)
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context = $Context
if ($Context -eq 'docker-desktop') { throw 'Capacity verification requires an isolated cluster.' }
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $script:StateRoot 'capacity' }
New-Item -ItemType Directory -Force $ReportDirectory | Out-Null
$project = Join-Path $script:RepositoryRoot 'backend/tests/NoCTF.Tests/NoCTF.Tests.csproj'
Invoke-Checked dotnet @('build',$project,'-c','Release')
$names = @('NOCTF_KUBERNETES_ACCEPTANCE','NOCTF_KUBERNETES_CONTEXT','NOCTF_KUBERNETES_TEST_IMAGE','NOCTF_KUBERNETES_PUBLIC_HOST','NOCTF_KUBERNETES_POD_COUNT','NOCTF_KUBERNETES_REPORT','NOCTF_KUBERNETES_IMAGE_PULL_SECRETS')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$runner = Get-KubeJson @('-n','noctf','get','statefulset','runner')
$runnerReplicas = $runner.spec.replicas
try {
    # Provider fixtures deliberately have no relational RuntimeInstance. Suspend
    # the business orphan reconciler on this isolated acceptance cluster.
    Invoke-Kube @('-n','noctf','scale','statefulset/runner','--replicas=0')
    if ($runnerReplicas -gt 0) { Invoke-Kube @('-n','noctf','wait','--for=delete','pod/runner-0','--timeout=120s') }
    $env:NOCTF_KUBERNETES_ACCEPTANCE='true'
    $env:NOCTF_KUBERNETES_CONTEXT=$Context
    $env:NOCTF_KUBERNETES_TEST_IMAGE=$Image
    $env:NOCTF_KUBERNETES_PUBLIC_HOST=$PublicHost
    $env:NOCTF_KUBERNETES_IMAGE_PULL_SECRETS=$ImagePullSecrets -join ','
    foreach ($stage in (($Counts -join ',') -split ',')) {
        $count = [int]$stage
        if ($count -lt 1 -or $count -gt 1000) { throw 'Counts must be between 1 and 1000.' }
        $nodes = Get-KubeJson @('get','nodes','-l','noctf.io/pod-pids-limit=256')
        $slots = ($nodes.items | Where-Object { $_.spec.PSObject.Properties.Name -notcontains 'unschedulable' -or -not $_.spec.unschedulable } | ForEach-Object { [int]$_.status.allocatable.pods } | Measure-Object -Sum).Sum
        $report = Join-Path $ReportDirectory "provider-$count.json"
        if ($slots * .8 -lt $count) {
            Save-Json $report @{context=$Context;targetPods=$count;completed=$false;reason='InsufficientPodSlots';allocatablePodSlots=$slots;safetyHeadroom=.2}
            Write-Warning "Stage $count was not run: fewer than required Pod slots with 20% headroom. Report: $report"
            continue
        }
        $env:NOCTF_KUBERNETES_POD_COUNT="$count"
        $env:NOCTF_KUBERNETES_REPORT=$report
        $dll = Join-Path $script:RepositoryRoot 'backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll'
        Invoke-Checked dotnet @($dll,'--treenode-filter','/*/NoCTF.Tests.Integration.Runtime/KubernetesDeploymentAcceptanceTests/*','--disable-logo','--no-ansi','--progress','off')
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) }
    Invoke-Kube @('-n','noctf','scale','statefulset/runner',"--replicas=$runnerReplicas")
    if ($runnerReplicas -gt 0) { Invoke-Kube @('-n','noctf','rollout','status','statefulset/runner','--timeout=180s') }
}
