[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousRedis = $env:ConnectionStrings__Redis
$previousNats = $env:ConnectionStrings__Nats
$previousPostgreSql = $env:ConnectionStrings__PostgreSql
$previousAuthenticationSigningKey = $env:Authentication__SigningKey
$previousSigningKey = $env:RunnerScoring__SigningKey
$previousCallbackBaseUrl = $env:RunnerScoring__CallbackBaseUrl
$previousWebhookBaseUrl = $env:Webhooks__PublicBaseUrl
$previousRunnerId = $env:Runner__Id
$previousRunnerPool = $env:Runner__Pool
$previousRunnerProvider = $env:Runner__Provider

try {
    # Generate against the production dependency graph. JasperFx's codegen CLI
    # does not connect to these placeholder endpoints.
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:ConnectionStrings__Redis = 'localhost:6379'
    $env:ConnectionStrings__Nats = 'nats://localhost:4222'
    $env:ConnectionStrings__PostgreSql = 'Host=localhost;Database=noctf_codegen;Username=postgres;Password=unused'
    $env:Authentication__SigningKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:RunnerScoring__SigningKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:RunnerScoring__CallbackBaseUrl = 'http://localhost:5080'
    $env:Webhooks__PublicBaseUrl = 'https://noctf.invalid'
    $env:Runner__Id = 'codegen-runner'
    $env:Runner__Pool = 'codegen'
    $env:Runner__Provider = 'Docker'

    Push-Location $backendRoot
    try {
        $generatedAttributesPath = Join-Path $backendRoot 'src\NoCTF.Host\Internal\Generated\WolverineHandlers\.gitattributes'
        $generatedAttributes = if (Test-Path -LiteralPath $generatedAttributesPath) {
            [IO.File]::ReadAllBytes($generatedAttributesPath)
        } else { $null }
        dotnet build .\src\NoCTF.Host\NoCTF.Host.csproj `
            -c $Configuration --no-restore -p:NoCtfGenerateWolverineHandlers=true -v:q
        if ($LASTEXITCODE -ne 0) {
            throw "Wolverine codegen bootstrap build failed (exit code $LASTEXITCODE)."
        }
        # Let JasperFx remove obsolete adapters after handler removals or signature changes.
        $cleanupOutput = dotnet run --project .\src\NoCTF.Host\NoCTF.Host.csproj `
            -c $Configuration --no-build --no-launch-profile -- codegen delete 2>&1
        $cleanupExitCode = $LASTEXITCODE
        $cleanupOutput | ForEach-Object { Write-Host $_ }
        if ($cleanupExitCode -ne 0 -or ($cleanupOutput -join "`n") -match '(?m)^ERROR:') {
            throw "Wolverine handler cleanup failed (exit code $cleanupExitCode)."
        }
        $output = dotnet run --project .\src\NoCTF.Host\NoCTF.Host.csproj `
            -c $Configuration --no-build --no-launch-profile -- codegen write 2>&1
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        # JasperFx can report a generation ERROR while the process exits zero.
        if ($exitCode -ne 0 -or ($output -join "`n") -match '(?m)^ERROR:') {
            throw "Wolverine handler generation failed (exit code $exitCode)."
        }
        if ($null -ne $generatedAttributes) {
            [IO.File]::WriteAllBytes($generatedAttributesPath, $generatedAttributes)
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ConnectionStrings__Redis = $previousRedis
    $env:ConnectionStrings__Nats = $previousNats
    $env:ConnectionStrings__PostgreSql = $previousPostgreSql
    $env:Authentication__SigningKey = $previousAuthenticationSigningKey
    $env:RunnerScoring__SigningKey = $previousSigningKey
    $env:RunnerScoring__CallbackBaseUrl = $previousCallbackBaseUrl
    $env:Webhooks__PublicBaseUrl = $previousWebhookBaseUrl
    $env:Runner__Id = $previousRunnerId
    $env:Runner__Pool = $previousRunnerPool
    $env:Runner__Provider = $previousRunnerProvider
}
