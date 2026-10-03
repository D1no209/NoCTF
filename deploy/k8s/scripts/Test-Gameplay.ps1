[CmdletBinding()]
param([ValidateSet('kind-noctf-dev')][string]$Context='kind-noctf-dev',
    [ValidatePattern('^(Ctf|Awd|Awdp|Koh)(,(Ctf|Awd|Awdp|Koh))*$')][string[]]$Modes=@('Ctf','Awd','Awdp','Koh'),
    [switch]$SkipBuild,
    [string]$SignalrBaseUrl,
    [string]$SecondSignalrBaseUrl)
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
$envNames=@('NOCTF_E2E_BASE_URL','NOCTF_E2E_ADMIN_USERNAME','NOCTF_E2E_ADMIN_PASSWORD','NOCTF_E2E_CONNECT_ADDRESS','NOCTF_E2E_TLS_SHA256','NOCTF_E2E_RUNTIME_IMAGE','NOCTF_E2E_TARGET_IMAGE','NOCTF_E2E_CHECKER_IMAGE','NOCTF_E2E_RUN_SUFFIX','NOCTF_E2E_SIGNALR_BASE_URL','NOCTF_E2E_SIGNALR_SECOND_BASE_URL')
$previous=@{}
foreach ($name in $envNames) { $previous[$name]=[Environment]::GetEnvironmentVariable($name) }
$project=Join-Path $script:RepositoryRoot 'backend/tests/NoCTF.E2E/NoCTF.E2E.csproj'
if (-not $SkipBuild) { Invoke-Checked dotnet @('build',$project,'-c','Release') }
try {
    $credentials=Get-Content -Raw (Join-Path $script:StateRoot 'noctf-dev/secrets.json') | ConvertFrom-Json
    $tls=Get-Content -Raw (Join-Path $script:StateRoot 'noctf-dev/tls.json') | ConvertFrom-Json
    $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem($tls.stringData.'tls.crt')
    $env:NOCTF_E2E_BASE_URL='https://noctf.local:8443'
    $env:NOCTF_E2E_SIGNALR_BASE_URL=$SignalrBaseUrl
    $env:NOCTF_E2E_SIGNALR_SECOND_BASE_URL=$SecondSignalrBaseUrl
    $env:NOCTF_E2E_RUN_SUFFIX=[Guid]::NewGuid().ToString('N').Substring(0,8)
    $env:NOCTF_E2E_ADMIN_USERNAME='admin'
    $env:NOCTF_E2E_ADMIN_PASSWORD=$credentials.stringData.'seed-admin-password'
    $env:NOCTF_E2E_CONNECT_ADDRESS='127.0.0.1'
    $env:NOCTF_E2E_TLS_SHA256=$certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
    foreach ($mode in (($Modes -join ',') -split ',')) {
        $fixtures=switch ($mode) {
            Ctf { @{'runtime'='ctf-runtime'} }
            Awd { @{'runtime'='awd-runtime';'checker'='awd-checker'} }
            Awdp { @{'target'='awdp-target';'checker'='awdp-checker'} }
            Koh { @{'runtime'='koh-runtime'} }
        }
        foreach ($entry in $fixtures.GetEnumerator()) {
            $image="localhost:5001/noctf-e2e/$($entry.Value):dev"
            $fixture=Join-Path $script:RepositoryRoot "backend/tests/NoCTF.E2E/fixtures/$($entry.Value)"
            Invoke-Checked docker @('build','-t',$image,$fixture)
            Invoke-Checked docker @('push',$image)
            [Environment]::SetEnvironmentVariable("NOCTF_E2E_$($entry.Key.ToUpperInvariant())_IMAGE",$image)
        }
        $dll=Join-Path $script:RepositoryRoot 'backend/tests/NoCTF.E2E/bin/Release/net10.0/NoCTF.E2E.dll'
        Invoke-Checked dotnet @($dll,'--treenode-filter',"/*/NoCTF.E2E/${mode}FullBoundaryTests/*",'--disable-logo','--no-ansi','--progress','off')
    }
} finally {
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) }
}
