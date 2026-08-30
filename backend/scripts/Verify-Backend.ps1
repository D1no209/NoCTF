[CmdletBinding()]
param(
    [switch]$SkipOpenApi,
    [switch]$RequireDockerIntegration
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
$previousDockerRequirement = $env:NOCTF_REQUIRE_DOCKER_INTEGRATION
function Complete-NativeStep([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
    Write-Host "[PASSED] $Step"
}

function Test-DockerDaemon {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        return $false
    }

    & $docker.Source info --format '{{.ServerVersion}}' *> $null
    return $LASTEXITCODE -eq 0
}

Push-Location $backendRoot
try {
    $testAssembly = Join-Path $backendRoot 'tests\NoCTF.Tests\bin\Debug\net10.0\NoCTF.Tests.dll'
    $dockerRequired = $RequireDockerIntegration -or [string]::Equals(
        $previousDockerRequirement,
        'true',
        [StringComparison]::OrdinalIgnoreCase)

    Write-Host '[RUN] Build'
    dotnet build .\NoCTF.slnx --no-restore -m:1
    Complete-NativeStep 'Build'

    Write-Host '[RUN] Non-Integration tests'
    dotnet $testAssembly `
        --treenode-filter '/*/*/*/*[Category!=Integration]' `
        --minimum-expected-tests 1
    Complete-NativeStep 'Non-Integration tests'

    $integrationResult = 'SKIPPED'
    if (Test-DockerDaemon) {
        $env:NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true'
        Write-Host '[RUN] Integration tests (Docker available)'
        dotnet $testAssembly `
            --treenode-filter '/*/*/*/*[Category=Integration]' `
            --minimum-expected-tests 38
        Complete-NativeStep 'Integration tests'
        $integrationResult = 'PASSED'
    }
    elseif ($dockerRequired) {
        throw 'Integration tests require Docker, but the Docker daemon is unavailable.'
    }
    else {
        Write-Warning '[SKIPPED] Integration tests: Docker daemon is unavailable.'
    }

    Write-Host '[RUN] EF migration drift check'
    dotnet ef migrations has-pending-model-changes `
        --project .\src\NoCTF.Infrastructure\NoCTF.Infrastructure.csproj `
        --startup-project .\src\NoCTF.API\NoCTF.API.csproj
    Complete-NativeStep 'EF migration drift check'

    if (-not $SkipOpenApi) {
        Write-Host '[RUN] OpenAPI export'
        dotnet run --project .\src\NoCTF.API\NoCTF.API.csproj `
            --no-launch-profile `
            -- `
            --export-openapi
        Complete-NativeStep 'OpenAPI export'

        Write-Host '[RUN] OpenAPI artifact drift check'
        git -C (Split-Path -Parent $backendRoot) diff --exit-code -- `
            backend/artifacts/openapi/swagger.json `
            backend/src/NoCTF.API/wwwroot/openapi/v1.json
        Complete-NativeStep 'OpenAPI artifact drift check'

        Write-Host '[RUN] Frontend API client generation'
        Push-Location .\src\NoCTF.API\ClientApp
        try {
            bun install --frozen-lockfile
            Complete-NativeStep 'Frontend dependency install'
            bun run api:gen
            Complete-NativeStep 'Frontend API client generation'
        }
        finally {
            Pop-Location
        }

        Write-Host '[RUN] Frontend API client drift check'
        git -C (Split-Path -Parent $backendRoot) diff --exit-code -- `
            backend/src/NoCTF.API/ClientApp/app/api
        Complete-NativeStep 'Frontend API client drift check'
    }
    else {
        Write-Warning '[SKIPPED] OpenAPI export: -SkipOpenApi was specified.'
    }

    Write-Host '[RUN] Git diff check'
    git -C (Split-Path -Parent $backendRoot) diff --check
    Complete-NativeStep 'Git diff check'

    Write-Host "[SUMMARY] Non-Integration=PASSED; Integration=$integrationResult; EF=PASSED; OpenAPI=$(if ($SkipOpenApi) { 'SKIPPED' } else { 'PASSED' }); Diff=PASSED"
}
finally {
    $env:NOCTF_REQUIRE_DOCKER_INTEGRATION = $previousDockerRequirement
    Pop-Location
}
