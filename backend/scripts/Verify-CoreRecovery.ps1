param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path $PSScriptRoot -Parent
$testAssembly = Join-Path $backendRoot "tests/NoCTF.Tests/bin/$Configuration/net10.0/NoCTF.Tests.dll"
Push-Location $backendRoot
try {
    dotnet build NoCTF.slnx -c $Configuration --no-restore -m:1 -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Core recovery build failed.' }
    foreach ($testClass in @('RedisRunnerCapacityGateTests', '*HistoricalAdjudication*')) {
        $started = [System.Diagnostics.Stopwatch]::StartNew()
        dotnet $testAssembly --treenode-filter "/*/*/$testClass/*" --minimum-expected-tests 1
        if ($LASTEXITCODE -ne 0) { throw "Core recovery tests failed: $testClass" }
        Write-Host "$testClass completed in $($started.Elapsed.TotalSeconds.ToString('F2')) seconds."
    }
} finally {
    Pop-Location
}
