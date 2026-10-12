[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Kind,
    [Parameter(Mandatory)][string]$Helm,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidatePattern('^noctf-gc-[a-z0-9-]+$')][string]$ClusterName = 'noctf-gc-acceptance',
    [switch]$Resume
)
$ErrorActionPreference = 'Stop'
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($Resume) {
    $owner = Get-Content -LiteralPath (Join-Path $output 'ownership.json') -Raw | ConvertFrom-Json
    if ($owner.cluster -ne $ClusterName -or $owner.purpose -ne 'synthetic-runtime-acceptance') { throw 'Ownership mismatch.' }
} else {
    if (Test-Path -LiteralPath $output) { throw 'Use a new output directory.' }
    $existingClusters = @(& $Kind get clusters)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory existing clusters.' }
    if ($ClusterName -in $existingClusters) { throw 'Refusing to claim an existing cluster.' }
    New-Item -ItemType Directory -Path $output | Out-Null
    @{ cluster = $ClusterName; purpose = 'synthetic-runtime-acceptance'; createdAt = [DateTimeOffset]::UtcNow } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'ownership.json') -Encoding utf8NoBOM
}
$config = Join-Path $output 'kubeconfig.yaml'
$context = "kind-$ClusterName"
if ((& $Kind version) -notmatch 'v0.33.0') { throw 'kind v0.33.0 is required.' }
if (-not $Resume) {
if ($ClusterName -in @(& $Kind get clusters)) { throw 'Refusing to reuse an existing cluster.' }
foreach ($port in 31000..31031) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    try { $listener.Start() } finally { $listener.Stop() }
}
$mappings = (31000..31031 | ForEach-Object { "  - containerPort: $_`n    hostPort: $_`n    listenAddress: 127.0.0.1" }) -join "`n"
@"
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
networking:
  disableDefaultCNI: true
  podSubnet: 10.246.0.0/16
  serviceSubnet: 10.98.0.0/16
nodes:
- role: control-plane
  image: kindest/node:v1.36.4@sha256:099e049362a1526b2db71494e1947aae99bd16290d7c895f2b7ea312e3cbfaed
  kubeadmConfigPatches:
  - |
    kind: ClusterConfiguration
    apiServer:
      extraArgs:
        service-node-port-range: 31000-31031
  extraPortMappings:
$mappings
- role: worker
  image: kindest/node:v1.36.4@sha256:099e049362a1526b2db71494e1947aae99bd16290d7c895f2b7ea312e3cbfaed
"@ | Set-Content -LiteralPath (Join-Path $output 'kind.yaml') -Encoding utf8NoBOM
Invoke-Checked $Kind @('create','cluster','--name',$ClusterName,'--config',(Join-Path $output 'kind.yaml'),'--kubeconfig',$config)
}
Invoke-Checked $Helm @('upgrade','--install','cilium','oci://quay.io/cilium/charts/cilium@sha256:a7c12d330dd96bfcda3bf057b24be8f36566c34868265f930f776dff6f42d838','--namespace','kube-system','--kubeconfig',$config,'--kube-context',$context,'--set','ipam.mode=kubernetes','--set','policyEnforcementMode=always','--set','kubeProxyReplacement=false','--set','operator.replicas=1','--wait','--timeout','5m')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'apply','-f',(Join-Path $repository 'deploy/k8s/platform/cilium/non-runtime-allow.yaml'))
Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'wait','--for=condition=Ready','nodes','--all','--timeout=300s')
$metricsPath = Join-Path $output 'metrics-server.yaml'
Invoke-WebRequest 'https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.9.0/components.yaml' -OutFile $metricsPath
if ((Get-FileHash $metricsPath -Algorithm SHA256).Hash -ne '1cec29a5267809306a2c6ec74a3e449abbb705b4a8beed0c8a1963910f72c79b') { throw 'Metrics Server checksum mismatch.' }
(Get-Content -Raw $metricsPath).Replace('        - --cert-dir=/tmp', "        - --kubelet-insecure-tls`n        - --cert-dir=/tmp") |
    Set-Content -LiteralPath $metricsPath -Encoding utf8NoBOM
Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'apply','-f',$metricsPath)
Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'-n','kube-system','rollout','status','deployment/metrics-server','--timeout=180s')
$node = "$ClusterName-worker"
$settings = ((Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'get','--raw',"/api/v1/nodes/$node/proxy/configz") | Out-String) | ConvertFrom-Json -AsHashtable).kubeletconfig
$settings['apiVersion'] = 'kubelet.config.k8s.io/v1beta1'
$settings['kind'] = 'KubeletConfiguration'
$settings['podPidsLimit'] = 256
$settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $output 'kubelet.json') -Encoding utf8NoBOM
Invoke-Checked docker @('cp',(Join-Path $output 'kubelet.json'),"${node}:/var/lib/kubelet/config.yaml")
Invoke-Checked docker @('exec',$node,'systemctl','restart','kubelet')
$verified = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Seconds 2
    $raw = & kubectl --kubeconfig $config --context $context get --raw "/api/v1/nodes/$node/proxy/configz" 2>$null
    if ($LASTEXITCODE -eq 0 -and (($raw | Out-String | ConvertFrom-Json).kubeletconfig.podPidsLimit -eq 256)) { $verified = $true; break }
}
if (-not $verified) { throw 'Actual kubelet Pod PID limit was not attested.' }
Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'label','node',$node,'noctf.io/pod-pids-limit=256','--overwrite')
Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'apply','-f',(Join-Path $repository 'deploy/k8s/base/00-runtime-namespace.yaml'))
@'
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: noctf-runtime-baseline-deny
  namespace: runtime
  labels:
    noctf.io/purpose: runtime-baseline
spec:
  podSelector: {}
  policyTypes: [Ingress, Egress]
  ingress: []
  egress: []
---
apiVersion: v1
kind: ResourceQuota
metadata:
  name: acceptance-budget
  namespace: runtime
spec:
  hard:
    requests.cpu: '4'
    requests.memory: 4Gi
    pods: '16'
'@ | Set-Content -LiteralPath (Join-Path $output 'runtime.yaml') -Encoding utf8NoBOM
Invoke-Checked kubectl @('--kubeconfig',$config,'--context',$context,'apply','-f',(Join-Path $output 'runtime.yaml'))
# The pre-existing local registry is read-only input; only this cluster's nodes
# receive a registry mapping. It is never restarted, reconfigured or cleaned up.
@'
server = "http://noctf-dev-registry:5000"
[host."http://noctf-dev-registry:5000"]
  capabilities = ["pull", "resolve"]
'@ | Set-Content -LiteralPath (Join-Path $output 'hosts.toml') -Encoding utf8NoBOM
foreach ($ownedNode in @(& $Kind get nodes --name $ClusterName)) {
    Invoke-Checked docker @('exec',$ownedNode,'mkdir','-p','/etc/containerd/certs.d/localhost:5001')
    Invoke-Checked docker @('cp',(Join-Path $output 'hosts.toml'),"${ownedNode}:/etc/containerd/certs.d/localhost:5001/hosts.toml")
}
@{ cluster = $ClusterName; context = $context; kubeconfig = $config; createdAt = [DateTimeOffset]::UtcNow; purpose = 'synthetic-runtime-acceptance'; podPidsLimit = 256 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'ownership.json') -Encoding utf8NoBOM
Write-Host "Isolated Runner cluster ready: $context. Default kubeconfig was not changed."
