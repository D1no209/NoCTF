[CmdletBinding()]
param([ValidateSet('kind-noctf-dev')][string]$Context='kind-noctf-dev')
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
$credentials=Get-Content -Raw (Join-Path $script:StateRoot 'noctf-dev/secrets.json') | ConvertFrom-Json
$origin='https://127.0.0.1:8443'
$headers=@{Host='noctf.local:8443'}
$auth=Invoke-RestMethod "$origin/api/v1/auth/login" -Method Post -Headers $headers -SkipCertificateCheck -NoProxy -TimeoutSec 30 -ContentType application/json -Body (@{login='admin';password=$credentials.stringData.'seed-admin-password'} | ConvertTo-Json)
$headers.Authorization='Bearer '+$auth.accessToken
$stopped=0
$page=Invoke-RestMethod "$origin/api/v1/admin/competitions" -Headers $headers -SkipCertificateCheck -NoProxy -TimeoutSec 30
foreach ($competition in $page.items) {
    if ($competition.title -match 'full-boundary E2E' -and $competition.status -in @('Running','Paused')) {
        Invoke-RestMethod "$origin/api/v1/admin/competitions/$($competition.id)/status" -Method Put -Headers $headers -SkipCertificateCheck -NoProxy -TimeoutSec 30 -ContentType application/json -Body '{"status":"Finished"}' | Out-Null
        $stopped++
    }
}
Write-Host "Finished $stopped local E2E competitions."
