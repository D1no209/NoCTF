[CmdletBinding()]
param([ValidateSet('noctf-dev')][string]$ClusterName = 'noctf-dev',
    [string]$EnvoyChartRepository = 'oci://docker.io/envoyproxy/gateway-helm')
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context = "kind-$ClusterName"
$kind = Get-LockedTool kind
$helm = Get-LockedTool helm
$state = Join-Path $script:StateRoot $ClusterName
New-Item -ItemType Directory -Force $state | Out-Null
$existing = Invoke-Checked $kind @('get','clusters')
if ($ClusterName -notin $existing) {
    $info = (Invoke-Checked docker @('info','--format','{{json .}}') | Out-String) | ConvertFrom-Json
    if ($info.NCPU -lt 8 -or $info.MemTotal -lt 16GB) { throw 'The full local stack requires at least 8 CPUs and 16 GiB assigned to Docker Desktop.' }
    $ports = @(8080,8443) + @(30002..30127)
    foreach ($port in $ports) {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
        try { $listener.Start() } catch { throw "Local port $port is occupied." } finally { $listener.Stop() }
    }
    $mapping = (@("  - containerPort: 30000`n    hostPort: 8080`n    listenAddress: 127.0.0.1", "  - containerPort: 30001`n    hostPort: 8443`n    listenAddress: 127.0.0.1") + @(30002..30127 | ForEach-Object { "  - containerPort: $_`n    hostPort: $_`n    listenAddress: 127.0.0.1" })) -join "`n"
    # Every kind node sees the same VM capacity. Bound each worker's allocatable
    # resources so the platform and Runtime cannot independently claim the VM.
    $memoryMi = [int][Math]::Floor($info.MemTotal / 1MB)
    $workerMemory = [int][Math]::Floor($memoryMi * .35)
    $reservedMemory = $memoryMi - $workerMemory
    $reservedCpu = [Math]::Max(1, [int]$info.NCPU - [int][Math]::Floor($info.NCPU * .35))
    $patch = @"
    kind: KubeletConfiguration
    podPidsLimit: 256
    kubeReserved:
      cpu: "$reservedCpu"
      memory: "${reservedMemory}Mi"
"@
    $config = @"
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
networking:
  disableDefaultCNI: true
  podSubnet: 10.245.0.0/16
  serviceSubnet: 10.97.0.0/16
nodes:
- role: control-plane
  image: kindest/node:v1.36.4@sha256:099e049362a1526b2db71494e1947aae99bd16290d7c895f2b7ea312e3cbfaed
  kubeadmConfigPatches:
  - |
    kind: ClusterConfiguration
    apiServer:
      extraArgs:
        service-node-port-range: 30000-30127
  extraPortMappings:
$mapping
- role: worker
  image: kindest/node:v1.36.4@sha256:099e049362a1526b2db71494e1947aae99bd16290d7c895f2b7ea312e3cbfaed
  labels:
    noctf.io/node-role: platform
  kubeadmConfigPatches:
  - |
$patch
- role: worker
  image: kindest/node:v1.36.4@sha256:099e049362a1526b2db71494e1947aae99bd16290d7c895f2b7ea312e3cbfaed
  labels:
    noctf.io/node-role: runtime
  kubeadmConfigPatches:
  - |
$patch
"@
    $configPath = Join-Path $state 'kind.yaml'
    $config | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
    $previousContext = Invoke-Checked kubectl @('config','current-context')
    try { Invoke-Checked $kind @('create','cluster','--name',$ClusterName,'--config',$configPath) }
    finally { Invoke-Checked kubectl @('config','use-context',$previousContext) | Out-Null }
}
Invoke-Checked $helm @('upgrade','--install','cilium','oci://quay.io/cilium/charts/cilium@sha256:a7c12d330dd96bfcda3bf057b24be8f36566c34868265f930f776dff6f42d838','--namespace','kube-system','--kube-context',$script:Context,'--set','ipam.mode=kubernetes','--set','policyEnforcementMode=always','--set','kubeProxyReplacement=false','--set','operator.replicas=1','--wait','--timeout','5m')
Invoke-Kube @('apply','-f',(Join-Path $script:RepositoryRoot 'deploy/k8s/platform/cilium/non-runtime-allow.yaml'))
Invoke-Kube @('wait','--for=condition=Ready','nodes','--all','--timeout=300s')
$metricsPath = Join-Path $state 'metrics-server.yaml'
Invoke-WebRequest 'https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.9.0/components.yaml' -OutFile $metricsPath
if ((Get-FileHash $metricsPath -Algorithm SHA256).Hash -ne '1cec29a5267809306a2c6ec74a3e449abbb705b4a8beed0c8a1963910f72c79b') { throw 'Metrics Server manifest checksum mismatch.' }
$metrics = (Get-Content -Raw $metricsPath).Replace('        - --cert-dir=/tmp','        - --kubelet-insecure-tls' + "`n" + '        - --cert-dir=/tmp')
$metrics | Set-Content -LiteralPath $metricsPath -Encoding utf8NoBOM
Invoke-Kube @('apply','-f',$metricsPath)
Invoke-Kube @('-n','kube-system','rollout','status','deployment/metrics-server','--timeout=180s')
Invoke-Checked $helm @('upgrade','--install','eg',"$EnvoyChartRepository@sha256:be034275b55deeddd6b7bc1f4da6eeb02b8efc418a4a29e2b1977851b24b1e63",'--namespace','envoy-gateway-system','--create-namespace','--kube-context',$script:Context,'--wait','--timeout','5m')
$info = (Invoke-Checked docker @('info','--format','{{json .}}') | Out-String) | ConvertFrom-Json
$memoryMi = [int][Math]::Floor($info.MemTotal / 1MB)
$reservedMemory = $memoryMi - [int][Math]::Floor($memoryMi * .35)
$reservedCpu = [Math]::Max(1, [int]$info.NCPU - [int][Math]::Floor($info.NCPU * .35))
$nodes = Get-KubeJson @('get','nodes','-l','noctf.io/node-role')
foreach ($node in $nodes.items) {
    $configuration = (Invoke-Kube @('get','--raw',"/api/v1/nodes/$($node.metadata.name)/proxy/configz") | Out-String) | ConvertFrom-Json
    # kubeadm JoinConfiguration does not apply a worker-specific component
    # KubeletConfiguration patch. Write and verify the actual local-node config.
    $settings = $configuration.kubeletconfig | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable
    $settings['apiVersion']='kubelet.config.k8s.io/v1beta1'
    $settings['kind']='KubeletConfiguration'
    $settings['podPidsLimit']=256
    $runtimeNode = $node.metadata.labels.'noctf.io/node-role' -eq 'runtime'
    # kind workers share one physical VM. The namespace quota bounds Runtime
    # requests to 50% of that VM; platform allocation is 35%, leaving headroom.
    # NodeMetrics remain observations, not a sum of independent physical nodes.
    $nodeReservedMemory = if ($runtimeNode) { [int][Math]::Ceiling($memoryMi * .1) } else { $reservedMemory }
    $nodeReservedCpu = if ($runtimeNode) { [Math]::Max(1,[int][Math]::Ceiling($info.NCPU * .1)) } else { $reservedCpu }
    $settings['kubeReserved']=@{cpu="$nodeReservedCpu";memory="${nodeReservedMemory}Mi"}
    $nodeFile = Join-Path $state "$($node.metadata.name)-kubelet.json"
    Save-Json $nodeFile $settings
    Invoke-Checked docker @('cp',$nodeFile,"$($node.metadata.name):/var/lib/kubelet/config.yaml")
    Invoke-Checked docker @('exec',$node.metadata.name,'systemctl','restart','kubelet')
    $verified = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Seconds 2
        $raw = & kubectl --context $script:Context get --raw "/api/v1/nodes/$($node.metadata.name)/proxy/configz" 2>$null
        if ($LASTEXITCODE -eq 0 -and (($raw | Out-String | ConvertFrom-Json).kubeletconfig.podPidsLimit -eq 256)) { $verified=$true; break }
    }
    if (-not $verified) { throw 'Actual kubelet Pod PID limit is not 256.' }
    if ($runtimeNode) {
        Invoke-Kube @('label','node',$node.metadata.name,'noctf.io/pod-pids-limit=256','--overwrite')
    }
}
Invoke-Kube @('apply','-f',(Join-Path $script:KubernetesRoot 'base/00-runtime-namespace.yaml'))
$quotaPath=Join-Path $state 'runtime-quota.json'
Save-Json $quotaPath @{apiVersion='v1';kind='ResourceQuota';metadata=@{name='noctf-dev-budget';namespace='runtime'};spec=@{hard=@{'requests.cpu'="$([int][Math]::Floor($info.NCPU * .5))";'requests.memory'="$([int][Math]::Floor($memoryMi * .5))Mi"}}}
Invoke-Kube @('apply','-f',$quotaPath)
# Local registry is deployment-owned and not part of any challenge's topology.
$registry = Invoke-Checked docker @('ps','-a','--filter','name=^/noctf-dev-registry$','--format','{{.Names}}')
if (-not $registry) {
    Invoke-Checked docker @('run','-d','--restart=unless-stopped','--name','noctf-dev-registry','--network','kind','-p','127.0.0.1:5001:5000','registry:3.0.0@sha256:6c5666b861f3505b116bb9aa9b25175e71210414bd010d92035ff64018f9457e')
} else { Invoke-Checked docker @('start','noctf-dev-registry') }
$hosts = @"
server = "http://noctf-dev-registry:5000"
[host."http://noctf-dev-registry:5000"]
  capabilities = ["pull", "resolve"]
"@
$hostsPath = Join-Path $state 'hosts.toml'
$hosts | Set-Content -LiteralPath $hostsPath -Encoding utf8NoBOM
foreach ($node in (Invoke-Checked $kind @('get','nodes','--name',$ClusterName))) {
    Invoke-Checked docker @('exec',$node,'mkdir','-p','/etc/containerd/certs.d/localhost:5001')
    Invoke-Checked docker @('cp',$hostsPath,"${node}:/etc/containerd/certs.d/localhost:5001/hosts.toml")
}
Save-Json (Join-Path $state 'bootstrap-lock.json') @{kind='0.33.0';kubernetes='1.36.4';cilium='1.20.2';envoyGateway='1.9.2';metricsServer='0.9.0';metricsManifestSha256=(Get-FileHash $metricsPath -Algorithm SHA256).Hash;context=$script:Context}
Write-Host "Cluster ready: $script:Context. Next: Deploy.ps1 -Context $script:Context -Environment kind -Initialize -Build"
