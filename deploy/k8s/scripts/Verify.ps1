[CmdletBinding()]
param([Parameter(Mandatory)][string]$Context,[ValidateSet('kind','production')][string]$Environment='kind')
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context = $Context
Invoke-Kube @('get','--raw','/apis/metrics.k8s.io/v1beta1/nodes') | Out-Null
$cilium = Get-KubeJson @('-n','kube-system','get','configmap','cilium-config')
if ($cilium.data.'enable-policy' -ne 'always') { throw 'Cilium policy enforcement is not always.' }
$dns = Get-KubeJson @('-n','kube-system','get','service','kube-dns')
$config = Get-KubeJson @('-n','noctf','get','configmap','noctf-config')
if ($config.data.Runtime__Kubernetes__ClusterDnsServiceAddress -ne $dns.spec.clusterIP) { throw 'Configured cluster DNS does not match kube-dns.' }
$nodes=Get-KubeJson @('get','nodes','-l','noctf.io/pod-pids-limit=256')
if (@($nodes.items).Count -eq 0) { throw 'No PID-attested Runtime node is available.' }
foreach ($node in $nodes.items) {
    $actual=(Invoke-Kube @('get','--raw',"/api/v1/nodes/$($node.metadata.name)/proxy/configz") | Out-String) | ConvertFrom-Json
    if ($actual.kubeletconfig.podPidsLimit -ne 256) { throw 'Runtime node PID configuration does not match its attestation.' }
}
$claims=Get-KubeJson @('-n','noctf','get','pvc')
if ($claims.items | Where-Object { $_.status.phase -ne 'Bound' }) { throw 'Platform storage preflight found an unbound PVC.' }
$pullSecrets=@($config.data.PSObject.Properties | Where-Object { $_.Name -like 'Runtime__Kubernetes__ImagePullSecrets__*' } | ForEach-Object { $_.Value })
foreach ($name in $pullSecrets) {
    $secret=Get-KubeJson @('-n','runtime','get','secret',$name)
    if ($secret.type -ne 'kubernetes.io/dockerconfigjson') { throw 'Runtime ImagePullSecrets must be registry credentials.' }
}
$runner = Get-KubeJson @('-n','noctf','get','statefulset','runner')
if ($runner.spec.replicas -ne 1) { throw 'Exactly one Runner owns a Kubernetes resource domain.' }
foreach ($resource in @('deployment/backend','deployment/worker','deployment/nats-exporter','statefulset/runner','statefulset/postgres','statefulset/redis','statefulset/rustfs','statefulset/nats','statefulset/loki','statefulset/prometheus','statefulset/grafana')) {
    Invoke-Kube @('-n','noctf','rollout','status',$resource,'--timeout=120s')
}
foreach ($permission in @(@('create','pods','runtime'),@('create','pods/exec','runtime'),@('get','pods/exec','runtime'),@('list','nodes','noctf'))) {
    $answer = Invoke-Kube @('auth','can-i',$permission[0],$permission[1],'-n',$permission[2],'--as=system:serviceaccount:noctf:noctf-runner')
    if ($answer -ne 'yes') { throw 'Runner RBAC preflight failed.' }
}
$gateway = Get-KubeJson @('-n','noctf','get','gateway','noctf')
if (-not ($gateway.status.conditions | Where-Object { $_.type -eq 'Programmed' -and $_.status -eq 'True' })) { throw 'Gateway is not Programmed.' }
if ($Environment -eq 'kind') {
    Invoke-Checked curl.exe @('--fail','--silent','--show-error','--insecure','--noproxy','*','--max-time','15','--resolve','noctf.local:8443:127.0.0.1','https://noctf.local:8443/health/ready')
}
Write-Host 'Infrastructure preflight passed. Gameplay, isolation, recovery and capacity require the acceptance suite.'
