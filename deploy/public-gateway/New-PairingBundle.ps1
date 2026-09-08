param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][string]$Image
)
$ErrorActionPreference = 'Stop'
if ($Image -notmatch '^[A-Za-z0-9./:_-]+@sha256:[a-f0-9]{64}$') { throw 'A CI-produced immutable image reference is required.' }
$bundlePath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $bundlePath) { throw 'Refusing to overwrite an existing pairing bundle.' }
New-Item -ItemType Directory -Path $bundlePath | Out-Null
if ($IsWindows) {
    $acl = Get-Acl -LiteralPath $bundlePath
    $acl.SetAccessRuleProtection($true, $false)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $bundlePath -AclObject $acl
} else {
    [IO.File]::SetUnixFileMode($bundlePath, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
}
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-GeneratedText([string]$RelativePath, [string]$Text) {
    $generatedPath = Join-Path $bundlePath $RelativePath
    [IO.File]::WriteAllText($generatedPath, $Text, $utf8)
    if (!$IsWindows) { [IO.File]::SetUnixFileMode($generatedPath, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
}
function New-Pair([string]$Name, [string]$ServerDirectory, [string]$ClientDirectory) {
    New-Item -ItemType Directory -Path (Join-Path $bundlePath $ServerDirectory), (Join-Path $bundlePath $ClientDirectory) | Out-Null
    $caKey = [Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=NoCTF $Name pairing CA", $caKey, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign, $true))
        $ca = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(1))
        try {
            Write-GeneratedText "$ServerDirectory/ca.crt" $ca.ExportCertificatePem()
            Write-GeneratedText "$ClientDirectory/ca.crt" $ca.ExportCertificatePem()
            # Keep the CA private key only in this protected local bundle for controlled renewal.
            Write-GeneratedText "$Name-ca.key" $caKey.ExportPkcs8PrivateKeyPem()
            foreach ($role in @('server', 'client')) {
                $leafKey = [Security.Cryptography.RSA]::Create(2048)
                try {
                    $dns = if ($role -eq 'server') { "noctf-$Name-gateway" } else { "noctf-$Name-connector" }
                    $leafRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=$dns", $leafKey, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
                    $names = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
                    $names.AddDnsName($dns)
                    $leafRequest.CertificateExtensions.Add($names.Build())
                    $leafRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
                    $oids = [Security.Cryptography.OidCollection]::new()
                    $oid = if ($role -eq 'server') { '1.3.6.1.5.5.7.3.1' } else { '1.3.6.1.5.5.7.3.2' }
                    [void]$oids.Add([Security.Cryptography.Oid]::new($oid))
                    $leafRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $true))
                    $leaf = $leafRequest.Create($ca, [DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddDays(90), [Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
                    try {
                        $folder = if ($role -eq 'server') { $ServerDirectory } else { $ClientDirectory }
                        Write-GeneratedText "$folder/$role.crt" $leaf.ExportCertificatePem()
                        Write-GeneratedText "$folder/$role.key" $leafKey.ExportPkcs8PrivateKeyPem()
                    } finally { $leaf.Dispose() }
                } finally { $leafKey.Dispose() }
            }
        } finally { $ca.Dispose() }
    } finally { $caKey.Dispose() }
}
New-Pair 'website' 'website-server' 'website-client'
New-Pair 'challenge' 'challenge-server' 'runner-credentials'
$websiteToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$challengeToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
Write-GeneratedText 'website-server.env' "NOCTF_GATEWAY_COMPONENT=server`nFRP_BIND_PORT=60998`nNOCTF_FRP_TOKEN=$websiteToken`n"
Write-GeneratedText 'challenge-server.env' "NOCTF_GATEWAY_COMPONENT=server`nFRP_BIND_PORT=60999`nNOCTF_FRP_TOKEN=$challengeToken`n"
Write-GeneratedText 'website-client.env' "NOCTF_GATEWAY_COMPONENT=website-client`nNOCTF_FRP_TOKEN=$websiteToken`n"
Write-GeneratedText 'runner-credentials/token' "$challengeToken`n"
Write-GeneratedText '.env' "NOCTF_GATEWAY_IMAGE=$Image`n"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'website-frps.toml.example') -Destination (Join-Path $bundlePath 'website-server/frps.toml')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'challenge-frps.toml.example') -Destination (Join-Path $bundlePath 'challenge-server/frps.toml')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'website-frpc.toml.example') -Destination (Join-Path $bundlePath 'website-client/frpc.toml')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'jump.compose.yml') -Destination $bundlePath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'website-client.compose.yml') -Destination $bundlePath
Write-Output "Pairing bundle created: $bundlePath"
Write-Output 'Transfer only each role directory and its environment file. Never transfer either CA private key to a server.'
