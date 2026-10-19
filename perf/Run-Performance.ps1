#Requires -Version 7.4
param(
    [ValidateRange(1,100000)][int]$Vus=500,
    [string]$Duration='10m',
    [ValidateRange(0.1,3600)][double]$ThinkTimeSeconds=5,
    [ValidateRange(1,86400)][double]$SubmissionIntervalSeconds=60,
    [ValidateRange(1,600000)][double]$ReadP95Ms=500,
    [ValidateRange(1,600000)][double]$SubmitP95Ms=1000,
    [ValidateRange(1,600000)][double]$EvaluationP95Ms=5000,
    [ValidateRange(1,300)][int]$EvaluationTimeoutSeconds=30,
    [bool]$Observability=$true,
    [ValidateSet('None','Counters','Trace')][string]$DiagnosticsMode='Trace',
    [ValidateRange(0,3600)][int]$WarmupSeconds=30,
    [ValidateRange(5,300)][int]$TraceSeconds=30,
    [ValidateRange(0,3600)][int]$TraceDelaySeconds=30,
    [ValidateRange(1,60)][int]$DiagnosticsIntervalSeconds=5,
    [ValidateRange(10,3600)][int]$PageDwellSeconds=60,
    [switch]$ServeFrontend,
    [ValidateRange(2,1440)][int]$AccessTokenMinutes=15,
    [ValidateRange(0,600)][int]$EntrySpreadSeconds=30,
    [ValidateSet('Participant','Fixed')][string]$Workload='Participant',
    [string]$ApplicationSnapshot='',
    [switch]$CaptureDatabasePlanning,
    [switch]$PrepareOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/support/Environment.ps1"
. "$PSScriptRoot/support/Prepare-TestData.ps1"
. "$PSScriptRoot/support/Verify-TestResults.ps1"
. "$PSScriptRoot/support/Diagnostics.ps1"
. "$PSScriptRoot/support/Measurement.ps1"
if($ApplicationSnapshot){
    $ApplicationSnapshot=(Resolve-Path -LiteralPath $ApplicationSnapshot -ErrorAction Stop).Path
    if(!(Test-Path -LiteralPath (Join-Path $ApplicationSnapshot 'NoCTF.Host.dll'))){throw 'ApplicationSnapshot 必须包含编译好的 NoCTF.Host.dll。'}
    if($Workload -ne 'Fixed'){throw '历史应用快照必须使用 Fixed 负载。'}
}
if ($Duration -notmatch '^(?:(?:[0-9]+(?:\.[0-9]+)?)(?:h|m|s|ms))+$') { throw 'Duration 必须使用 k6 时间格式，例如 45s、10m 或 1h30m。' }
# 使用 .NET 正则提取时间单位，保持与接受的 k6 格式一致。
$seconds=0.0
foreach ($part in [regex]::Matches($Duration,'([0-9]+(?:\.[0-9]+)?)(ms|h|m|s)')) {
    $multiplier=@{h=3600;m=60;s=1;ms=0.001}[$part.Groups[2].Value]
    $seconds += [double]::Parse($part.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)*$multiplier
}
if ($seconds -le 0) { throw 'Duration 必须大于零。' }
if($EntrySpreadSeconds*($Vus-1)/$Vus -ge $seconds){throw '入场分散时间必须允许全部 VU 在测量结束前开始操作，请缩短 EntrySpreadSeconds 或增加 Duration。'}
if($DiagnosticsMode -eq 'Trace' -and $seconds -le $TraceDelaySeconds+5){throw '测量时间必须大于 TraceDelaySeconds 加 5 秒，或选择 Counters/None 模式。'}
$interval=[Math]::Max($SubmissionIntervalSeconds,[Math]::Ceiling($Vus*60/960))
$runId=[DateTime]::UtcNow.ToString('yyyyMMddHHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6)
$reports=Join-Path $PSScriptRoot "results/$runId"
$null=New-Item -ItemType Directory -Path $reports -Force
$configuration=@{runId=$runId;vus=$Vus;duration=$Duration;durationSeconds=$seconds;thinkTimeSeconds=$ThinkTimeSeconds;requestedSubmissionIntervalSeconds=$SubmissionIntervalSeconds;submissionIntervalSeconds=$interval;readP95Ms=$ReadP95Ms;submitP95Ms=$SubmitP95Ms;evaluationP95Ms=$EvaluationP95Ms;evaluationTimeoutSeconds=$EvaluationTimeoutSeconds;startedAt=[DateTimeOffset]::UtcNow.ToString('O')}
$configuration.observability=$Observability
$configuration.diagnosticsMode=$DiagnosticsMode
$configuration.warmupSeconds=$WarmupSeconds
$configuration.traceSeconds=$TraceSeconds
$configuration.traceDelaySeconds=$TraceDelaySeconds
$configuration.diagnosticsIntervalSeconds=$DiagnosticsIntervalSeconds
$configuration.pageDwellSeconds=$PageDwellSeconds
$configuration.workload=$Workload
$configuration.scoreboardNotificationCoalesceMilliseconds=if($Workload -eq 'Participant'){500}else{0}
$configuration.applicationSnapshot=$ApplicationSnapshot
$configuration.captureDatabasePlanning=[bool]$CaptureDatabasePlanning
$configuration.serveFrontend=[bool]$ServeFrontend
$configuration.accessTokenMinutes=$AccessTokenMinutes
$configuration.entrySpreadSeconds=$EntrySpreadSeconds
Save-PerfJson $configuration (Join-Path $reports 'configuration.json')
Write-Host "运行标识：$runId；$Vus VU；测量时间 $Duration；提交间隔 $interval 秒"
$state=$null
$prepared=$false
try {
    $null=Get-Command bun -ErrorAction Stop
    Push-Location $PSScriptRoot
    try {
        Invoke-PerfNative bun @('install','--frozen-lockfile') (Join-Path $reports 'protocol-install.log')
        Invoke-PerfNative bun @('build','support/signalr-protocol.js','--target','browser','--outfile','.local/signalr-protocol.js') (Join-Path $reports 'protocol-build.log')
    } finally { Pop-Location }
    Initialize-PerfDiagnostics $PSScriptRoot $reports $configuration
    $state=Start-PerfEnvironment $PSScriptRoot $runId $reports $Observability ([bool]$ServeFrontend) $AccessTokenMinutes $ApplicationSnapshot ([bool]$CaptureDatabasePlanning)
    Write-PerfPhase $state data_preparation
    $data=Prepare-PerfTestData $state $Vus $seconds
    $configuration.dataFile=Join-Path $state.local 'data.json'
    $configuration.reportDirectory=$reports
    $configPath=Join-Path $state.local 'k6.json'
    Save-PerfJson $configuration $configPath
    $script=New-PerfWorkloadSnapshot $state $configuration $PSScriptRoot
    Save-PerfJson $configuration $configPath
    Invoke-PerfNative k6 @('inspect','-e',"PERF_CONFIG=$configPath",$script) (Join-Path $reports 'inspect.json')
    if ($PrepareOnly) {
        $prepared=$true
        Write-Host "环境和数据准备完成。k6 配置：$configPath"
        return
    }
    Invoke-PerfMeasurement $state $configuration $configPath $script $data $PSScriptRoot
} catch {
    $statePath=Join-Path $PSScriptRoot ".local/$runId/state.json"
    if ($null -eq $state -and (Test-Path -LiteralPath $statePath)) { $state=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable }
    if ($null -ne $state) {
        & docker compose --project-name $state.project --env-file $state.envFile -f $state.compose logs --no-color --tail 200 *> (Join-Path $reports 'dependencies.log')
    }
    throw
} finally {
    try {
        if($null -ne $state -and $state.ContainsKey('diagnosticsPid')){Stop-PerfDiagnostics $state}
    } finally {
        $configuration.endedAt=[DateTimeOffset]::UtcNow.ToString('O')
        Save-PerfJson $configuration (Join-Path $reports 'configuration.json')
        if($null -ne $state -and !$prepared){& (Join-Path $PSScriptRoot 'Stop-Performance.ps1') -RunId $runId}
        elseif($prepared){Write-Host "准备环境保留，停止命令：.\perf\Stop-Performance.ps1 -RunId $runId"}
    }
}
