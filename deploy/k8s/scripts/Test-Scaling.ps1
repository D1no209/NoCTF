[CmdletBinding()]
param([ValidateSet('kind-noctf-dev')][string]$Context='kind-noctf-dev')
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
$original=@{}
foreach ($role in @('backend','worker')) { $original[$role]=(Get-KubeJson @('-n','noctf','get','deployment',$role)).spec.replicas }
$forwarders=@()
foreach ($port in @(19080,19081)) {
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
    try { $listener.Start() } finally { $listener.Stop() }
}
try {
    Invoke-Kube @('-n','noctf','scale','deployment/backend','deployment/worker','--replicas=2')
    foreach ($role in @('backend','worker')) { Invoke-Kube @('-n','noctf','rollout','status',"deployment/$role",'--timeout=180s') }
    $pods=Get-KubeJson @('-n','noctf','get','pods','-l','app=backend')
    $ready=@($pods.items | Where-Object { $_.status.conditions | Where-Object { $_.type -eq 'Ready' -and $_.status -eq 'True' } } | Sort-Object { $_.metadata.name })
    if ($ready.Count -ne 2) { throw 'Scaling requires exactly two ready API Pods.' }
    for ($index=0;$index -lt 2;$index++) {
        $port=19080+$index
        $launch=@{FilePath=(Get-Command kubectl).Source;ArgumentList=@('--context',$Context,'-n','noctf','port-forward',"pod/$($ready[$index].metadata.name)","${port}:8080",'--address','127.0.0.1');PassThru=$true;RedirectStandardOutput=(Join-Path $script:StateRoot "scaling-forward-$index.log");RedirectStandardError=(Join-Path $script:StateRoot "scaling-forward-$index.err")}
        if ($IsWindows) { $launch['WindowStyle']='Hidden' }
        $forwarder=Start-Process @launch
        $forwarders+=$forwarder
        $connected=$false
        for ($attempt=0;$attempt -lt 40;$attempt++) {
            if ($forwarder.HasExited) { throw 'API port-forward exited before readiness.' }
            $probe=[Net.Sockets.TcpClient]::new()
            try { $probe.Connect('127.0.0.1',$port); $connected=$true; break } catch { Start-Sleep -Milliseconds 250 } finally { $probe.Dispose() }
        }
        if (-not $connected) { throw 'API port-forward did not become ready.' }
    }
    & "$PSScriptRoot/Test-Gameplay.ps1" -Context $Context -Modes Ctf -SignalrBaseUrl 'http://noctf.local:19080' -SecondSignalrBaseUrl 'http://noctf.local:19081'
    Save-Json (Join-Path $script:StateRoot 'scaling.json') @{context=$Context;apiReplicas=2;workerReplicas=2;runnerReplicas=1;scoreboardDeliveredToBothApiPods=$true;scope='CTF and SignalR notification receipt on each API Pod, not an exactly-once transport guarantee.'}
} finally {
    foreach ($forwarder in $forwarders) { if (-not $forwarder.HasExited) { Stop-Process -Id $forwarder.Id } }
    foreach ($role in @('backend','worker')) {
        Invoke-Kube @('-n','noctf','scale',"deployment/$role","--replicas=$($original[$role])")
        Invoke-Kube @('-n','noctf','rollout','status',"deployment/$role",'--timeout=180s')
    }
}
