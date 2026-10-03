[CmdletBinding()]
param([ValidateSet('kind-noctf-dev')][string]$Context='kind-noctf-dev',
    [string]$InputImage='localhost:5001/noctf-capacity:dev')
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
$names=@('NOCTF_KUBERNETES_INTEGRATION','NOCTF_KUBERNETES_CONTEXT','NOCTF_KUBERNETES_PUBLIC_HOST','NOCTF_KUBERNETES_POD_PIDS_LIMIT','NOCTF_KUBERNETES_CLUSTER_DNS','NOCTF_KUBERNETES_TEST_IMAGE')
$previous=@{}
foreach ($name in $names) { $previous[$name]=[Environment]::GetEnvironmentVariable($name) }
Invoke-Checked dotnet @('build',(Join-Path $script:RepositoryRoot 'backend/tests/NoCTF.Tests/NoCTF.Tests.csproj'),'-c','Release')
try {
    $dns=Get-KubeJson @('-n','kube-system','get','service','kube-dns')
    $env:NOCTF_KUBERNETES_INTEGRATION='true'
    $env:NOCTF_KUBERNETES_CONTEXT=$Context
    $env:NOCTF_KUBERNETES_PUBLIC_HOST='127.0.0.1'
    $env:NOCTF_KUBERNETES_POD_PIDS_LIMIT='256'
    $env:NOCTF_KUBERNETES_CLUSTER_DNS=$dns.spec.clusterIP
    $env:NOCTF_KUBERNETES_TEST_IMAGE=$InputImage
    $dll=Join-Path $script:RepositoryRoot 'backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll'
    foreach ($test in @('KubernetesNamedServicesIntegrationTests','KubernetesOneShotInputArchiveTests')) {
        Invoke-Checked dotnet @($dll,'--treenode-filter',"/*/NoCTF.Tests.Integration.Runtime/$test/*",'--disable-logo','--no-ansi','--progress','off')
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) }
}
