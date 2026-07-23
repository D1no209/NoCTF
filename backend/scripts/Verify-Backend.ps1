[CmdletBinding()]
param(
    [switch]$SkipOpenApi
)

$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path -Parent $PSScriptRoot
function Assert-NativeSuccess([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}
Push-Location $backendRoot
try {
    dotnet build .\NoCTF.slnx --no-restore -m:1
    Assert-NativeSuccess 'Build'
    dotnet test .\NoCTF.slnx -- --minimum-expected-tests 1
    Assert-NativeSuccess 'Test'
    dotnet ef migrations has-pending-model-changes `
        --project .\src\NoCTF.Infrastructure\NoCTF.Infrastructure.csproj `
        --startup-project .\src\NoCTF.API\NoCTF.API.csproj
    Assert-NativeSuccess 'EF migration drift check'
    if (-not $SkipOpenApi) {
        dotnet run --project .\src\NoCTF.API\NoCTF.API.csproj -- --export-openapi
        Assert-NativeSuccess 'OpenAPI export'
    }
    git -C (Split-Path -Parent $backendRoot) diff --check
    Assert-NativeSuccess 'Git diff check'
}
finally {
    Pop-Location
}
