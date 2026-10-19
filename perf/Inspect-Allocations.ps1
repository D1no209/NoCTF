#Requires -Version 7.4
param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$RunId)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. "$PSScriptRoot/support/Environment.ps1"
$trace=Join-Path $PSScriptRoot "results/$RunId/diagnostics/runtime.nettrace"
if(!(Test-Path -LiteralPath $trace)){throw '该运行没有保存运行时轨迹。'}
$output=Join-Path $PSScriptRoot "results/$RunId/diagnostics/allocation-callers"
$null=New-Item -ItemType Directory -Path $output -Force
Invoke-PerfNative dotnet @('run','--project',"$PSScriptRoot/analysis/NoCTF.PerfAnalysis.csproj",'-c','Release',
    '--no-restore','--',$trace,$output,'--allocation-stacks') (Join-Path $output 'analysis.log')
Write-Host "分配调用路径：$output/allocation-callers.json"
