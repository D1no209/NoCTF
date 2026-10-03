[CmdletBinding()]
param([ValidateSet('kind-noctf-dev')][string]$Context='kind-noctf-dev',
    [string]$Image='localhost:5001/noctf-capacity:dev')
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
$name='noctf-pid-check-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$path=Join-Path $script:StateRoot 'pid-check-pod.json'
Save-Json $path @{apiVersion='v1';kind='Pod';metadata=@{name=$name;namespace='runtime'};spec=@{restartPolicy='Never';automountServiceAccountToken=$false;nodeSelector=@{'noctf.io/pod-pids-limit'='256'};containers=@(@{name='probe';image=$Image;command=@('/bin/sh','-c','exec sleep 300');resources=@{requests=@{cpu='50m';memory='32Mi'};limits=@{cpu='100m';memory='64Mi'}}})}}
try {
    Invoke-Kube @('create','-f',$path)
    Invoke-Kube @('-n','runtime','wait','--for=condition=Ready',"pod/$name",'--timeout=120s')
    $pod=Get-KubeJson @('-n','runtime','get','pod',$name)
    $uid=$pod.metadata.uid.Replace('-','_')
    if ($uid -notmatch '^[a-f0-9_]+$' -or $pod.spec.nodeName -notmatch '^noctf-dev-') { throw 'PID check resolved outside the dedicated kind cluster.' }
    $group=(Invoke-Checked docker @('exec',$pod.spec.nodeName,'find','/sys/fs/cgroup','-type','d','-name',"*pod$uid.slice") | Select-Object -First 1).Trim()
    if ($group -notmatch '^/sys/fs/cgroup/.*pod[a-f0-9_]+\.slice$') { throw 'Pod cgroup was not resolved.' }
    $limit=[int](Invoke-Checked docker @('exec',$pod.spec.nodeName,'cat',"$group/pids.max"))
    if ($limit -ne 256) { throw 'Pod cgroup PID limit is not 256.' }
    # Deliberately bounded fork pressure: at most 270 children, each exits in 3s.
    # EAGAIN makes the shell fail; that exit is expected and is not an app failure.
    & kubectl --context $Context -n runtime exec $name -- /bin/sh -c 'i=0; while [ "$i" -lt 270 ]; do sleep 3 & i=$((i+1)); done; wait' 2>$null | Out-Null
    $events=(Invoke-Checked docker @('exec',$pod.spec.nodeName,'cat',"$group/pids.events") | Out-String)
    $peak=[int](Invoke-Checked docker @('exec',$pod.spec.nodeName,'cat',"$group/pids.peak"))
    if ($events -notmatch 'max\s+([1-9][0-9]*)' -or $peak -gt 256) { throw 'PID pressure did not demonstrate enforced rejection.' }
    Save-Json (Join-Path $script:StateRoot 'pid-limit.json') @{context=$Context;limit=$limit;peak=$peak;rejectedForks=[int]$Matches[1];passed=$true}
} finally {
    Invoke-Kube @('-n','runtime','delete','pod',$name,'--ignore-not-found','--wait=true','--timeout=60s')
}
