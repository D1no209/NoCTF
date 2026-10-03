[CmdletBinding()]
param([ValidateSet('kind-noctf-dev')][string]$Context='kind-noctf-dev')
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
$results=@{}
function Wait-Role {
    param([string]$Resource)
    Invoke-Kube @('-n','noctf','rollout','status',$Resource,'--timeout=180s')
}
function Restart-StatefulPod {
    param([string]$Name)
    $old=Get-KubeJson @('-n','noctf','get','pod',"$Name-0")
    Invoke-Kube @('-n','noctf','delete','pod',"$Name-0",'--wait=true')
    Wait-Role "statefulset/$Name"
    $new=Get-KubeJson @('-n','noctf','get','pod',"$Name-0")
    if ($new.metadata.uid -eq $old.metadata.uid) { throw "$Name did not restart." }
}
& "$PSScriptRoot/Verify.ps1" -Context $Context
$pvcBefore=Get-KubeJson @('-n','noctf','get','pvc')
$metrics=Get-KubeJson @('-n','kube-system','get','deployment','metrics-server')
try {
    Invoke-Kube @('-n','kube-system','scale','deployment/metrics-server','--replicas=0')
    $blocked=$false
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(90)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $runner=Get-KubeJson @('-n','noctf','get','pod','runner-0')
        if ($runner.status.conditions | Where-Object { $_.type -eq 'Ready' -and $_.status -eq 'False' }) { $blocked=$true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $blocked) { throw 'Runner did not reject stale Metrics observations.' }
    $results['staleMetricsRejectsReadiness']=$true
} finally {
    Invoke-Kube @('-n','kube-system','scale','deployment/metrics-server',"--replicas=$($metrics.spec.replicas)")
    Invoke-Kube @('-n','kube-system','rollout','status','deployment/metrics-server','--timeout=180s')
}
Wait-Role 'statefulset/runner'
Restart-StatefulPod runner
$results['runnerStableIdentityReacquired']=$true
Invoke-Kube @('-n','noctf','rollout','restart','deployment/worker')
Wait-Role 'deployment/worker'
$results['workerRestartReady']=$true
$before=(Invoke-Kube @('-n','noctf','exec','nats-0','--','wget','-qO-','http://127.0.0.1:8222/jsz') | Out-String) | ConvertFrom-Json
Restart-StatefulPod nats
$after=(Invoke-Kube @('-n','noctf','exec','nats-0','--','wget','-qO-','http://127.0.0.1:8222/jsz') | Out-String) | ConvertFrom-Json
if ($before.streams -ne $after.streams -or $before.consumers -ne $after.consumers) { throw 'JetStream metadata counts changed after persistent restart.' }
$results['jetStreamMetadataRetained']=@{streams=$after.streams;consumers=$after.consumers}
Invoke-Kube @('-n','noctf','exec','redis-0','--','redis-cli','FLUSHDB') | Out-Null
$results['cacheLossInjected']=$true
foreach ($role in @('deployment/backend','deployment/worker','statefulset/runner')) { Wait-Role $role }
$pvcAfter=Get-KubeJson @('-n','noctf','get','pvc')
foreach ($old in $pvcBefore.items) {
    $current=$pvcAfter.items | Where-Object { $_.metadata.name -eq $old.metadata.name }
    if (-not $current -or $current.metadata.uid -ne $old.metadata.uid -or $current.spec.volumeName -ne $old.spec.volumeName) { throw 'Recovery replaced a PVC or its bound volume.' }
}
$results['volumesRetained']=$true
$results['scope']='Dependency/role recovery; rerun gameplay to prove business results after failure.'
Save-Json (Join-Path $script:StateRoot 'recovery.json') $results
& "$PSScriptRoot/Verify.ps1" -Context $Context
