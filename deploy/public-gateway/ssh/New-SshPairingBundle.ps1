param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][string]$ServerHost,
    [Parameter(Mandatory = $true)][string]$RunnerSourceAddress,
    [Parameter(Mandatory = $true)][string]$ServerImage,
    [Parameter(Mandatory = $true)][string]$ClientImage,
    [Parameter(Mandatory = $true)][string]$RelayImage,
    [ValidateRange(32768, 60999)][int]$ControlPort = 60999,
    [ValidateRange(32768, 60999)][int]$FirstPublicPort = 40000,
    [ValidateRange(1, 256)][int]$PublicPortCount = 256,
    [int[]]$ReservedPorts = @(36632, 60998, 60999)
)
$ErrorActionPreference = 'Stop'
foreach ($image in @($ServerImage, $ClientImage, $RelayImage)) {
    if ($image -notmatch '^[A-Za-z0-9./:_-]+@sha256:[a-f0-9]{64}$') { throw '镜像必须使用 CI 产出的 sha256 引用。' }
}
if ($ServerHost.Length -gt 253 -or $ServerHost -notmatch '^[A-Za-z0-9.-]+$' -or [Uri]::CheckHostName($ServerHost) -eq [UriHostNameType]::Unknown) { throw '跳板主机必须是 IPv4 地址或域名。' }
$sourceNetwork = $null
$sourceAddress = $null
if ($RunnerSourceAddress -notmatch '^[0-9A-Fa-f:./]+$' -or (![Net.IPAddress]::TryParse($RunnerSourceAddress, [ref]$sourceAddress) -and ![Net.IPNetwork]::TryParse($RunnerSourceAddress, [ref]$sourceNetwork))) { throw 'Runner 来源必须是运维确认的地址或 CIDR。' }
$ports = @($FirstPublicPort..($FirstPublicPort + $PublicPortCount - 1))
if ($ports[-1] -gt 60999 -or $ports -contains $ControlPort -or @($ports | Where-Object { $ReservedPorts -contains $_ }).Count -gt 0) { throw '公网端口池越界或包含保留端口。' }
$keygen = (Get-Command ssh-keygen -ErrorAction Stop).Source
$bundlePath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $bundlePath) { throw '拒绝覆盖已有配对目录。' }
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
foreach ($role in @('server','client')) { New-Item -ItemType Directory -Path (Join-Path $bundlePath $role) | Out-Null }
function Write-PairingFile([string]$Relative, [string]$Content) {
    $path = Join-Path $bundlePath $Relative
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
    if (!$IsWindows) { [IO.File]::SetUnixFileMode($path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
}
foreach ($role in @('server','client')) {
    $keyPath = Join-Path $bundlePath "$role/key"
    $info = [Diagnostics.ProcessStartInfo]::new($keygen)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($arg in @('-q','-t','ed25519','-N','','-C',"noctf-gateway-$role",'-f',$keyPath)) { $info.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw '专用 SSH 配对密钥生成失败。' }
    } finally { $process.Dispose() }
}
$hostPublic = [IO.File]::ReadAllText((Join-Path $bundlePath 'server/key.pub')).Trim()
$clientPublic = [IO.File]::ReadAllText((Join-Path $bundlePath 'client/key.pub')).Trim()
Write-PairingFile 'server/authorized_keys' "restrict,port-forwarding $clientPublic`n"
if (!$IsWindows) {
    # sshd checks this public-key file while running as the restricted forwarding user.
    [IO.File]::SetUnixFileMode((Join-Path $bundlePath 'server/authorized_keys'), [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead)
}
Write-PairingFile 'client/known_hosts' "[$($ServerHost.ToLowerInvariant())]:$ControlPort $hostPublic`n"
$allowed = ($ports | ForEach-Object { "0.0.0.0:$_" }) -join ' '
Write-PairingFile 'server/sshd_config' @"
Port $ControlPort
ListenAddress 0.0.0.0
HostKey /config/key
AuthorizedKeysFile /config/authorized_keys
AllowUsers noctf@$RunnerSourceAddress
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
PermitRootLogin no
PermitEmptyPasswords no
AuthenticationMethods publickey
AllowTcpForwarding remote
AllowStreamLocalForwarding no
GatewayPorts clientspecified
PermitListen $allowed
PermitOpen none
MaxSessions 0
PermitTTY no
PermitTunnel no
AllowAgentForwarding no
X11Forwarding no
PermitUserRC no
ForceCommand /bin/false
LoginGraceTime 10
MaxAuthTries 2
MaxStartups 3:30:6
ClientAliveInterval 3
ClientAliveCountMax 3
Compression no
LogLevel ERROR
"@
Write-PairingFile 'client/client.conf' @"
Host gateway
  HostName $($ServerHost.ToLowerInvariant())
  Port $ControlPort
  User noctf
  IdentityFile /config/key
  UserKnownHostsFile /config/known_hosts
  GlobalKnownHostsFile /dev/null
  StrictHostKeyChecking yes
  IdentitiesOnly yes
  IdentityAgent none
  BatchMode yes
  RequestTTY no
  ExitOnForwardFailure yes
  ServerAliveInterval 3
  ServerAliveCountMax 3
  ConnectTimeout 5
  ConnectionAttempts 1
  Compression no
  ControlMaster yes
  ControlPath /control/master
  ControlPersist no
"@
Write-PairingFile 'client.env' "AUTOSSH_GATETIME=0`nAUTOSSH_POLL=3`n"
Write-PairingFile '.env' "NOCTF_SSH_SERVER_IMAGE=$ServerImage`nNOCTF_SSH_CLIENT_IMAGE=$ClientImage`nNOCTF_GATEWAY_RELAY_IMAGE=$RelayImage`n"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'server.compose.yml') -Destination $bundlePath
Write-Output "已生成专用配对目录：$bundlePath"
Write-Output "仅生成配置，未部署。控制端口 $ControlPort；公网端口池 $FirstPublicPort-$($ports[-1])。"
Write-Output 'server 目录只交给跳板；client 目录只交给对应 Runner。不要上传 Git，不使用个人 SSH 密钥。'
