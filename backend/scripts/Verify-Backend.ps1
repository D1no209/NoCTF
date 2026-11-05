[CmdletBinding()]
param(
    [switch]$SkipOpenApi,
    [switch]$RequireDockerIntegration,
    [switch]$AllowGeneratedChanges
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

function Get-GeneratedContentState([string[]]$Paths) {
    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            "$path $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
        }
        elseif (Test-Path -LiteralPath $path -PathType Container) {
            Get-ChildItem -LiteralPath $path -File -Recurse | Sort-Object FullName | ForEach-Object {
                "$($_.FullName) $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
            }
        }
    }
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
            --maximum-parallel-tests 1 `
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
        --project .\src\NoCTF.Persistence.PostgreSql\NoCTF.Persistence.PostgreSql.csproj
    Complete-NativeStep 'EF migration drift check'

    if (-not $SkipOpenApi) {
        $openApiPaths = @('artifacts/openapi/swagger.json', 'src/NoCTF.API/wwwroot/openapi/v1.json')
        $previousOpenApi = @(Get-GeneratedContentState $openApiPaths)
        Write-Host '[RUN] OpenAPI export'
        dotnet run --project .\src\NoCTF.Host\NoCTF.Host.csproj `
            --no-launch-profile `
            -- `
            --export-openapi
        Complete-NativeStep 'OpenAPI export'

        Write-Host '[RUN] OpenAPI artifact drift check'
        if ($AllowGeneratedChanges) {
            if (@(Compare-Object $previousOpenApi @(Get-GeneratedContentState $openApiPaths) -SyncWindow 0).Count -ne 0) {
                throw 'OpenAPI generation changed file content. Generate the artifacts, then rerun verification.'
            }
        }
        else {
            git -C (Split-Path -Parent $backendRoot) diff --exit-code -- `
                backend/artifacts/openapi/swagger.json `
                backend/src/NoCTF.API/wwwroot/openapi/v1.json
            Complete-NativeStep 'OpenAPI artifact drift check'
        }

        Write-Host '[RUN] Frontend API client generation'
        $previousSdk = @(Get-GeneratedContentState @('src/NoCTF.API/ClientApp/app/api'))
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
        if ($AllowGeneratedChanges) {
            if (@(Compare-Object $previousSdk @(Get-GeneratedContentState @('src/NoCTF.API/ClientApp/app/api')) -SyncWindow 0).Count -ne 0) {
                throw 'SDK generation changed file content. Generate the artifacts, then rerun verification.'
            }
        }
        else {
            git -C (Split-Path -Parent $backendRoot) diff --exit-code -- `
                backend/src/NoCTF.API/ClientApp/app/api
            Complete-NativeStep 'Frontend API client drift check'
        }
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
