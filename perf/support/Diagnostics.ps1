function Add-PerfRecord([string]$Path, $Value) {
    ConvertTo-Json $Value -Depth 40 -Compress | Add-Content -LiteralPath $Path -Encoding utf8
}

function Write-PerfPhase($State, [string]$Phase, [DateTimeOffset]$Utc = [DateTimeOffset]::UtcNow) {
    $utcText=$Utc.ToUniversalTime().ToString('O')
    Add-PerfRecord (Join-Path $State.reports 'phases.jsonl') @{phase=$Phase;utc=$utcText}
    if ($Phase -eq 'measurement') { Save-PerfJson @{utc=$utcText} (Join-Path $State.local 'measurement.json') }
}

function Initialize-PerfDiagnostics([string]$PerfRoot, [string]$Reports, $Configuration) {
    $directory=Join-Path $Reports 'diagnostics'
    $null=New-Item -ItemType Directory -Path $directory -Force
    $repository=Split-Path $PerfRoot
    $metadata=@{
        utc=[DateTimeOffset]::UtcNow.ToString('O'); configuration=$Configuration.Clone()
        deployment='Windows framework-dependent Release Host; Linux Docker dependencies; same-host k6'
        os=[Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        logicalProcessors=[Environment]::ProcessorCount
        cpu=@(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed)
        memoryBytes=(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
        gitCommit=(& git -C $repository rev-parse HEAD); dirty=@(& git -C $repository status --porcelain)
        environment=@{}
    }
    foreach($name in @('DOTNET_gcServer','DOTNET_TieredCompilation','DOTNET_TieredPGO','COMPlus_gcServer','COMPlus_TieredCompilation','COMPlus_TieredPGO')) {
        $metadata.environment[$name]=[Environment]::GetEnvironmentVariable($name)
    }
    $sourceRoot=[IO.Path]::GetFullPath($PerfRoot)
    $sourceHashes=[Collections.Generic.List[object]]::new()
    foreach($sourceFile in (Get-ChildItem -LiteralPath $sourceRoot -Recurse -File)){
        if($sourceFile.FullName -match '\\(\.local|results|bin|obj|node_modules)\\'){continue}
        $sourceHashes.Add(@{path=[IO.Path]::GetRelativePath($sourceRoot,$sourceFile.FullName);sha256=(Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash})
    }
    $metadata.sourceHashes=$sourceHashes.ToArray()
    Save-PerfJson $metadata (Join-Path $directory 'environment.json')
    & powercfg /getactivescheme *> (Join-Path $directory 'power-plan.txt')
    Invoke-PerfNative dotnet @('--info') (Join-Path $directory 'dotnet-info.txt')
    Invoke-PerfNative docker @('info','--format','{"serverVersion":{{json .ServerVersion}},"os":{{json .OperatingSystem}},"architecture":{{json .Architecture}},"cpus":{{json .NCPU}},"memoryBytes":{{json .MemTotal}}}') (Join-Path $directory 'docker-info.json')
    Invoke-PerfNative k6 @('version') (Join-Path $directory 'k6-version.txt')
    if ($Configuration.diagnosticsMode -eq 'None') { return }
    Invoke-PerfNative dotnet @('tool','restore','--tool-manifest',(Join-Path $PerfRoot '.config/dotnet-tools.json')) (Join-Path $directory 'tools-restore.log')
    if($Configuration.diagnosticsMode -eq 'Trace'){
        Invoke-PerfNative dotnet @('build',(Join-Path $PerfRoot 'analysis/NoCTF.PerfAnalysis.csproj'),'-c','Release','--nologo') (Join-Path $directory 'analyzer-build.log')
    }
    Push-Location $PerfRoot
    try {
        foreach ($tool in @('dotnet-counters','dotnet-trace','dotnet-stack')) {
            Invoke-PerfNative dotnet @('tool','run',$tool,'--','--version') (Join-Path $directory "$tool-version.txt")
            $command=if($tool -eq 'dotnet-stack'){'report'}else{'collect'}
            Invoke-PerfNative dotnet @('tool','run',$tool,'--',$command,'--help') (Join-Path $directory "$tool-help.txt")
        }
        Invoke-PerfNative dotnet @('tool','run','dotnet-trace','--','list-profiles') (Join-Path $directory 'trace-profiles.txt')
    } finally { Pop-Location }
}

function Start-PerfDiagnostics($State, $Configuration, [string]$ConfigPath, [string]$PerfRoot) {
    if($Configuration.diagnosticsMode -eq 'None'){ return }
    if($State.ContainsKey('diagnosticsPid')){
        $previous=Get-Process -Id $State.diagnosticsPid -ErrorAction SilentlyContinue
        if($null -ne $previous -and $previous.StartTime.ToUniversalTime().Ticks -eq ([DateTimeOffset]$State.diagnosticsStartTime).UtcTicks){throw '该环境已有诊断采集器正在运行。'}
    }
    foreach($marker in @('diagnostics.stop','diagnostics.ready')){
        $markerPath=Join-Path $State.local $marker
        if(Test-Path -LiteralPath $markerPath){Remove-Item -LiteralPath $markerPath}
    }
    $directory=Join-Path $State.reports 'diagnostics'
    Save-PerfJson @{observability=$State.observability;overrides=@{environment='Production';roles=@('Api','Worker');metricsListener=$State.metricsUrl;otlpExport=$false};secretOverrideNames=@($State.settings.Keys | Where-Object {$_ -match 'Password|Key|Secret|ConnectionStrings'})} (Join-Path $directory 'host-configuration.json')
    $symbols=Join-Path $directory 'symbols'
    $null=New-Item -ItemType Directory -Path $symbols -Force
    $binaries=$State.binaryDirectory
    Get-ChildItem $binaries -File | Where-Object { $_.Name -like 'NoCTF.*' -and $_.Extension -in @('.pdb','.dll') } | Copy-Item -Destination $symbols
    $arguments=@('-NoProfile','-File',('"'+(Join-Path $PerfRoot 'support/Collect-Diagnostics.ps1')+'"'),'-StateFile',('"'+$State.stateFile+'"'),'-ConfigFile',('"'+$ConfigPath+'"'))
    $process=Start-Process (Get-Command pwsh).Source -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $directory 'collector.log') -RedirectStandardError (Join-Path $directory 'collector-error.log')
    $State.diagnosticsPid=$process.Id
    $State.diagnosticsStartTime=$process.StartTime.ToUniversalTime().ToString('O')
    Save-PerfJson $State $State.stateFile
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(30)
    while (!(Test-Path (Join-Path $State.local 'diagnostics.ready'))) {
        if ($process.HasExited -or [DateTimeOffset]::UtcNow -gt $deadline) { throw "诊断采集器未能启动。诊断目录：$directory" }
        Start-Sleep -Milliseconds 250
    }
}

function Stop-PerfDiagnostics($State) {
    if (!$State.ContainsKey('diagnosticsPid')) { return }
    New-Item -ItemType File -Path (Join-Path $State.local 'diagnostics.stop') -Force | Out-Null
    $process=Get-Process -Id $State.diagnosticsPid -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        if ($process.StartTime.ToUniversalTime().Ticks -ne ([DateTimeOffset]$State.diagnosticsStartTime).UtcTicks) { throw '诊断采集进程身份发生变化。' }
        if (!$process.WaitForExit(55000)) { $process.Kill($true); throw '诊断采集器未在 55 秒内停止。' }
    }
    $statusPath=Join-Path $State.reports 'diagnostics/status.json'
    if (!(Test-Path $statusPath)) { throw '诊断采集器未生成完成状态。' }
    $status=Get-Content $statusPath -Raw | ConvertFrom-Json -AsHashtable
    if (!$status.complete) { throw "诊断采集不完整：$($status.error)" }
}
