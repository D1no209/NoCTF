[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Context,
    [ValidateSet('kind','production')][string]$Environment = 'kind',
    [string]$Image = 'noctf-host:dev',
    [switch]$Initialize,
    [switch]$Build
)
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context = $Context
if ($Context -eq 'docker-desktop') { throw 'Use the dedicated kind-noctf-dev context; docker-desktop is preserved.' }
if ($Environment -eq 'production' -and $Image -notmatch '@sha256:[a-f0-9]{64}$') { throw 'Production requires an immutable Host image digest.' }
if ($Environment -eq 'production') {
    $rendered = Invoke-Checked kubectl @('kustomize',(Join-Path $script:KubernetesRoot 'overlays/production')) | Out-String
    if ($rendered.Contains('.invalid')) { throw 'Replace production placeholder domains and cluster settings before deploying.' }
}
if ($Build) {
    if ($Environment -ne 'kind') { throw 'Build is only for local kind verification.' }
    Invoke-Checked docker @('build','-f',(Join-Path $script:RepositoryRoot 'backend/Dockerfile'),'--target','host','-t',$Image,$script:RepositoryRoot)
    $kind = Get-LockedTool kind
    Invoke-Checked $kind @('load','docker-image',$Image,'--name','noctf-dev')
}
# Establish namespaces and policies before creating any platform workload.
foreach ($file in @('namespace.yaml','00-runtime-namespace.yaml','01-runtime-networkpolicy.yaml','networkpolicy.yaml','runner-rbac.yaml')) {
    Invoke-Kube @('apply','-f',(Join-Path $script:KubernetesRoot "base/$file"))
}
if ($Environment -eq 'kind') { Initialize-LocalSecrets }
Invoke-Kube @('-n','noctf','get','secret','noctf-secrets','noctf-tls','-o','name') | Out-Null
Apply-Rendered (Join-Path $script:KubernetesRoot "overlays/$Environment") $Image -SuspendApplications:$Initialize
foreach ($name in @('postgres','redis','nats','rustfs','loki')) {
    Invoke-Kube @('-n','noctf','rollout','status',"statefulset/$name",'--timeout=300s')
}
if ($Initialize) {
    # First install creates the S3 bucket before Host startup migration.
    Invoke-Kube @('-n','noctf','scale','deployment/backend','deployment/worker','statefulset/runner','--replicas=0')
    Invoke-Kube @('-n','noctf','delete','job','storage-init','--ignore-not-found','--wait=true')
    Apply-Rendered (Join-Path $script:KubernetesRoot "init/$Environment") $Image
    Wait-Initialization
    Apply-Rendered (Join-Path $script:KubernetesRoot "overlays/$Environment") $Image
}
if ($Build -and -not $Initialize) {
    # A locally rebuilt :dev tag does not change the Pod template.
    foreach ($resource in @('deployment/backend','deployment/worker','statefulset/runner')) {
        Invoke-Kube @('-n','noctf','rollout','restart',$resource)
    }
}
foreach ($resource in @('deployment/backend','deployment/worker','statefulset/runner')) {
    Invoke-Kube @('-n','noctf','rollout','status',$resource,'--timeout=300s')
}
& "$PSScriptRoot/Verify.ps1" -Context $Context -Environment $Environment
