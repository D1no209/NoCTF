Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:KubernetesRoot = Split-Path $PSScriptRoot -Parent
$script:RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $script:KubernetesRoot '../../'))
$script:StateRoot = Join-Path $script:RepositoryRoot '.codex/k8s'
$script:Diagnosing = $false
$script:DiagnosticReported = $false

function Show-SafeFailureDiagnostics {
    $contextVariable=Get-Variable Context -Scope Script -ErrorAction SilentlyContinue
    if (-not $script:Diagnosing -and -not $script:DiagnosticReported -and $contextVariable -and $contextVariable.Value) {
        $script:DiagnosticReported=$true
        try { & (Join-Path $PSScriptRoot 'Diagnose.ps1') -Context $contextVariable.Value }
        catch { Write-Warning 'Automatic redacted cluster diagnostics were unavailable.' }
    }
}

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        $failedExit=$LASTEXITCODE
        Show-SafeFailureDiagnostics
        throw "$Executable failed (exit $failedExit)."
    }
}
function Invoke-Kube {
    param([string[]]$Arguments)
    if (-not $script:Context) { throw 'An explicit Kubernetes context is required.' }
    Invoke-Checked kubectl (@('--context', $script:Context) + $Arguments)
}
function Get-KubeJson {
    param([string[]]$Arguments)
    (Invoke-Kube ($Arguments + @('-o', 'json')) | Out-String) | ConvertFrom-Json -Depth 100
}
function Save-Json {
    param([string]$Path, $Value)
    New-Item -ItemType Directory -Force (Split-Path $Path -Parent) | Out-Null
    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
function Get-LockedTool {
    param([ValidateSet('kind','helm')][string]$Name)
    $directory = Join-Path $script:StateRoot 'tools'
    New-Item -ItemType Directory -Force $directory | Out-Null
    if (-not $IsWindows) { throw 'Bootstrap runs in PowerShell 7 on Windows; Linux clusters use the documented pinned dependencies.' }
    $target = Join-Path $directory "$Name.exe"
    if ((Test-Path $target) -and (Get-Item $target).Length -gt 0) {
        if ($Name -eq 'kind' -and (Get-FileHash $target -Algorithm SHA256).Hash -ne '4b22adaa135368c5a465d56bbd8e520cbea87272a06ca00b6078e7b81515c9fc') { throw 'Cached kind checksum mismatch.' }
        if ($Name -eq 'helm' -and (Get-FileHash $target -Algorithm SHA256).Hash -ne 'a18c49a4cd16f8b162031159eff6b4d657e04ec2df0c2be5544ad11ddf8fae79') { throw 'Cached Helm checksum mismatch.' }
        return $target
    }
    if ($Name -eq 'kind') {
        $url = 'https://github.com/kubernetes-sigs/kind/releases/download/v0.33.0/kind-windows-amd64'
        Invoke-WebRequest $url -OutFile $target
        $checksum = (Invoke-WebRequest "$url.sha256sum").Content
        if ($checksum -is [byte[]]) { $checksum = [Text.Encoding]::UTF8.GetString($checksum) }
        $expected = '4b22adaa135368c5a465d56bbd8e520cbea87272a06ca00b6078e7b81515c9fc'
        if ((Get-FileHash $target -Algorithm SHA256).Hash -ne $expected) { throw 'kind checksum mismatch.' }
    } else {
        $archive = Join-Path $directory 'helm.zip'
        $url = 'https://get.helm.sh/helm-v3.19.0-windows-amd64.zip'
        Invoke-WebRequest $url -OutFile $archive
        $checksum = (Invoke-WebRequest "$url.sha256sum").Content
        if ($checksum -is [byte[]]) { $checksum = [Text.Encoding]::UTF8.GetString($checksum) }
        $expected = '6488630c2e5d5945ed990fa02fd9e99f9c6792cdbcd79eb264b6cfb90179d2d1'
        if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Helm checksum mismatch.' }
        Expand-Archive -LiteralPath $archive -DestinationPath $directory -Force
        Copy-Item -LiteralPath (Join-Path $directory 'windows-amd64/helm.exe') -Destination $target
    }
    return $target
}
function Apply-Rendered {
    param([string]$Overlay, [string]$Image, [switch]$SuspendApplications)
    if ($Image -notmatch '^[a-zA-Z0-9][a-zA-Z0-9./:@_-]+$') { throw 'Invalid image reference.' }
    $manifest = (Invoke-Checked kubectl @('kustomize', $Overlay) | Out-String).Replace('image: noctf-host:dev', "image: $Image")
    $documents = $manifest -split '(?m)^---\s*$'
    $config = $documents | Where-Object { $_ -match '(?m)^kind: ConfigMap\r?$' -and $_ -match '(?m)^  name: noctf-config\r?$' }
    if ($config) {
        $checksum = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($config -join "`n")))).ToLowerInvariant()
        $manifest = ($documents | ForEach-Object {
            if ($_ -match '(?m)^  name: (backend|worker|runner)\r?$' -and $_ -match '(?m)^kind: (Deployment|StatefulSet)\r?$') {
                [regex]::Replace($_, '(?m)^    metadata:\r?$', "    metadata:`n      annotations:`n        noctf.io/config-checksum: $checksum")
            } else { $_ }
        }) -join "`n---`n"
    }
    if ($SuspendApplications) {
        $manifest = (($manifest -split '(?m)^---\s*$') | ForEach-Object {
            if ($_ -match '(?m)^  name: (backend|worker|runner)\r?$' -and $_ -match '(?m)^kind: (Deployment|StatefulSet)\r?$') {
                [regex]::Replace($_, '(?m)^  replicas: \d+\r?$', '  replicas: 0')
            } else { $_ }
        }) -join "`n---`n"
    }
    $path = Join-Path $script:StateRoot 'rendered.yaml'
    New-Item -ItemType Directory -Force $script:StateRoot | Out-Null
    $manifest | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    Invoke-Kube @('apply', '-f', $path)
}
function New-RandomSecret {
    [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
}
function Restore-LocalSecretFile {
    param([string]$Name,[string]$Path)
    $json=(& kubectl --context $script:Context -n noctf get secret $Name --ignore-not-found -o json | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the existing development Secret.' }
    if ([string]::IsNullOrWhiteSpace($json)) { return $false }
    $secret=$json | ConvertFrom-Json
    $values=@{}
    foreach ($property in $secret.data.PSObject.Properties) {
        $values[$property.Name]=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($property.Value))
    }
    Save-Json $Path @{apiVersion='v1';kind='Secret';metadata=@{name=$Name;namespace='noctf'};type=$secret.type;stringData=$values}
    return $true
}
function Wait-Initialization {
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $jobs = Get-KubeJson @('-n','noctf','get','jobs','storage-init')
        $complete = 0
        foreach ($job in $jobs.items) {
            if ($job.status.PSObject.Properties.Name -contains 'conditions') {
                if ($job.status.conditions | Where-Object { $_.type -eq 'Failed' -and $_.status -eq 'True' }) {
                    throw "Initialization job $($job.metadata.name) failed. Applications remain suspended; run Diagnose.ps1."
                }
                if ($job.status.conditions | Where-Object { $_.type -eq 'Complete' -and $_.status -eq 'True' }) { $complete++ }
            }
        }
        if ($complete -eq 1) { return }
        Start-Sleep -Seconds 2
    }
    throw 'Initialization timed out; applications remain suspended.'
}
function Initialize-LocalSecrets {
    $directory = Join-Path $script:StateRoot 'noctf-dev'
    New-Item -ItemType Directory -Force $directory | Out-Null
    $path = Join-Path $directory 'secrets.json'
    if (-not (Test-Path $path) -and -not (Restore-LocalSecretFile 'noctf-secrets' $path)) {
        $values = @{}
        foreach ($key in @('jwt-secret','db-password','seed-admin-password','runner-scoring-key','s3-secret-key','grafana-admin-password')) {
            $values[$key] = New-RandomSecret
        }
        $values['s3-access-key'] = (New-RandomSecret).Substring(0,20).ToUpperInvariant()
        $values['email-verification-encryption-key'] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        Save-Json $path @{apiVersion='v1';kind='Secret';metadata=@{name='noctf-secrets';namespace='noctf'};type='Opaque';stringData=$values}
        # Local files contain credentials. Restrict access to the current Windows user.
        if ($IsWindows) {
            $account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
            Invoke-Checked icacls @($directory, '/inheritance:r', '/grant:r', "${account}:(OI)(CI)F") | Out-Null
        }
    }
    $existing = & kubectl --context $script:Context -n noctf get secret noctf-secrets --ignore-not-found -o name
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing Secret.' }
    if (-not $existing) { Invoke-Kube @('apply', '-f', $path) }
    $current = Get-KubeJson @('-n','noctf','get','secret','noctf-secrets')
    if ($current.data.PSObject.Properties.Name -notcontains 's3-access-key') {
        $local = Get-Content -Raw $path | ConvertFrom-Json -AsHashtable
        $local.stringData['s3-access-key'] = (New-RandomSecret).Substring(0,20).ToUpperInvariant()
        $local.stringData['s3-secret-key'] = New-RandomSecret
        Save-Json $path $local
        $patchPath = Join-Path $directory 'storage-secret-patch.json'
        Save-Json $patchPath @{stringData=@{'s3-access-key'=$local.stringData['s3-access-key'];'s3-secret-key'=$local.stringData['s3-secret-key']}}
        Invoke-Kube @('-n','noctf','patch','secret','noctf-secrets','--type=merge','--patch-file',$patchPath)
    }
    $tlsPath = Join-Path $directory 'tls.json'
    if (-not (Test-Path $tlsPath) -and -not (Restore-LocalSecretFile 'noctf-tls' $tlsPath)) {
        $rsa = [Security.Cryptography.RSA]::Create(2048)
        try {
            $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=noctf.local',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
            $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
            $san.AddDnsName('noctf.local'); $san.AddDnsName('files.noctf.local'); $san.AddIpAddress([Net.IPAddress]::Loopback)
            $request.CertificateExtensions.Add($san.Build())
            $cert = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddYears(1))
            Save-Json $tlsPath @{apiVersion='v1';kind='Secret';metadata=@{name='noctf-tls';namespace='noctf'};type='kubernetes.io/tls';stringData=@{'tls.crt'=$cert.ExportCertificatePem();'tls.key'=$rsa.ExportPkcs8PrivateKeyPem()}}
        } finally { $rsa.Dispose() }
    }
    $tls = & kubectl --context $script:Context -n noctf get secret noctf-tls --ignore-not-found -o name
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect TLS Secret.' }
    if (-not $tls) { Invoke-Kube @('apply', '-f', $tlsPath) }
    if ($IsWindows) {
        $account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        Invoke-Checked icacls @($directory, '/inheritance:r', '/grant:r', "${account}:(OI)(CI)F") | Out-Null
    }
    Write-Host "Local credentials: $path (admin password: seed-admin-password)."
}
