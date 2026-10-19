#Requires -Version 7.4
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$RunId, [string]$QueryFile='', [switch]$Projection, [switch]$Admission)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/support/Environment.ps1"
$state=Get-Content (Join-Path $PSScriptRoot ".local/$RunId/state.json") -Raw | ConvertFrom-Json -AsHashtable
if($state.project -ne "noctf-perf-$RunId"){throw 'Compose 项目标识不一致。'}
$running=@(Invoke-PerfCompose $state @('ps','--status','running','-q'))
if($running.Count){throw '离线数据库诊断要求该测试环境已经停止。'}
if($Admission -and $Projection){throw 'Admission 与 Projection 不能同时使用。'}
$inspectCode=$Projection -or $Admission
if($inspectCode -and $QueryFile){throw '代码诊断与 QueryFile 不能同时使用。'}
$directory=Join-Path $state.reports 'database-plans'
$null=New-Item -ItemType Directory -Path $directory -Force
if($inspectCode){Invoke-PerfNative dotnet @('build',"$PSScriptRoot/database/NoCTF.PerfDatabase.csproj",'-c','Release') (Join-Path $directory 'tool-build.log')}
try {
    $start=@('up','-d','--wait','--wait-timeout','60')
    if(!$inspectCode){$start+='postgres'}
    Invoke-PerfCompose $state $start
    if($inspectCode){
        $data=Get-Content (Join-Path $state.local 'data.json') -Raw | ConvertFrom-Json -AsHashtable
        $inputFile=Join-Path $state.local 'database-inspection.json'
        Save-PerfJson @{PostgreSql="Host=127.0.0.1;Port=$(Get-PerfPort $state postgres 5432);Database=noctf;Username=noctf;Password=$($state.password)";
            Redis="127.0.0.1:$(Get-PerfPort $state redis 6379)";Nats="nats://127.0.0.1:$(Get-PerfPort $state nats 4222)";
            CompetitionId=$data.competitionId;Admission=[bool]$Admission} $inputFile
        Invoke-PerfNative dotnet @("$PSScriptRoot/database/bin/Release/net10.0/NoCTF.PerfDatabase.dll",$inputFile,$directory) (Join-Path $directory 'projection.log')
    }
    $sql=if($QueryFile){Get-Content -LiteralPath $QueryFile -Raw}else{
        "SELECT COALESCE(json_agg(row_to_json(s)), '[]') FROM (SELECT queryid,calls,plans,total_plan_time,mean_plan_time,total_exec_time,mean_exec_time,query FROM pg_stat_statements WHERE query LIKE 'SELECT %' AND calls>100 ORDER BY total_exec_time DESC LIMIT 10) s;"
    }
    $arguments=@('compose','--project-name',$state.project,'--env-file',$state.envFile,'-f',$state.compose,
        'exec','-T','-e',"PGPASSWORD=$($state.password)",'-e','PGOPTIONS=-c default_transaction_read_only=on',
        'postgres','psql','-X','-U','noctf','-d','noctf','-v','ON_ERROR_STOP=1','-At','-c',$sql)
    if(!$inspectCode){Invoke-PerfNative docker $arguments (Join-Path $directory 'query-result.json')}
} finally { Invoke-PerfCompose $state @('down') }
Write-Host "离线数据库诊断完成：$directory"
