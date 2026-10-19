#Requires -Version 7.4
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$RunId)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/support/Environment.ps1"
. "$PSScriptRoot/support/Diagnostics.ps1"
. "$PSScriptRoot/support/Measurement.ps1"
. "$PSScriptRoot/support/Verify-TestResults.ps1"
$local=Join-Path $PSScriptRoot ".local/$RunId"
$state=Get-Content (Join-Path $local 'state.json') -Raw | ConvertFrom-Json -AsHashtable
try {
$configPath=Join-Path $local 'k6.json'
$config=Get-Content $configPath -Raw | ConvertFrom-Json -AsHashtable
$data=Get-Content $config.dataFile -Raw | ConvertFrom-Json -AsHashtable
if($data.users.Count -ne $config.vus){throw '准备账号数量与配置不一致。'}
$process=Get-Process -Id $state.hostPid -ErrorAction Stop
if($process.StartTime.ToUniversalTime().Ticks -ne ([DateTimeOffset]$state.hostStartTime).UtcTicks){throw 'Host 进程身份不一致。'}
$facts=Invoke-PerfHttp $state GET "/api/v1/admin/competitions/$($data.competitionId)/gameplay-facts?limit=1" -Token ADMIN
if($facts.total -ne 0){throw '准备比赛已有 GameplayFact，必须为正式测试准备新比赛。'}
if(Test-Path (Join-Path $local 'measurement.json')){throw '该运行已经进入过正式测量，请准备新比赛。'}
if(Test-Path (Join-Path $state.reports 'diagnostics/status.json')){
    $reports=[IO.Path]::GetFullPath($state.reports)
    $archive=Join-Path $reports ('preparation-attempt-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))
    $null=New-Item -ItemType Directory -Path $archive
    foreach($name in @('diagnostics','k6.log','phases.jsonl','configuration.json','dependencies.log')){
        $source=[IO.Path]::GetFullPath((Join-Path $reports $name))
        if(!$source.StartsWith($reports+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '诊断归档路径超出报告目录。'}
        if(Test-Path -LiteralPath $source){Move-Item -LiteralPath $source -Destination $archive}
    }
}
if(!$config.ContainsKey('workload')){$config.workload='Participant'}
if(!$config.ContainsKey('pageDwellSeconds')){$config.pageDwellSeconds=60}
Initialize-PerfDiagnostics $PSScriptRoot $state.reports $config
$script=New-PerfWorkloadSnapshot $state $config $PSScriptRoot
Save-PerfJson $config $configPath
Invoke-PerfNative k6 @('inspect','-e',"PERF_CONFIG=$configPath",$script) (Join-Path $state.reports 'inspect.json')
Invoke-PerfMeasurement $state $config $configPath $script $data $PSScriptRoot
}
finally {
    if(Get-Variable config -ErrorAction SilentlyContinue){
        $config.endedAt=[DateTimeOffset]::UtcNow.ToString('O')
        Save-PerfJson $config (Join-Path $state.reports 'configuration.json')
    }
    & (Join-Path $PSScriptRoot 'Stop-Performance.ps1') -RunId $RunId
}
