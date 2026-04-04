[CmdletBinding()]
param(
    [switch]$SkipDatabase,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\RegistraceOvcina.Web\RegistraceOvcina.Web.csproj"
$databasePort = 5433
$connectionString = "Host=127.0.0.1;Port=$databasePort;Database=registrace_ovcina_development;Username=postgres;Password=postgres"
$startDatabaseScript = Join-Path $repoRoot "start-db.ps1"

if (-not (Test-Path $projectPath)) {
    throw "Could not find the web project at '$projectPath'."
}

if (-not $SkipDatabase) {
    & $startDatabaseScript
}

Write-Host "Starting Registrace Ovcina..." -ForegroundColor Green
Write-Host "App URL: http://localhost:5170" -ForegroundColor DarkGray
Write-Host "Database: $connectionString" -ForegroundColor DarkGray
Write-Host "Admin login: admin@ovcina.test / Pass123!" -ForegroundColor DarkGray
Write-Host "Registrant login: registrant@ovcina.test / Pass123!" -ForegroundColor DarkGray

$dotnetArguments = @(
    "run"
    "--project", $projectPath
    "--launch-profile", "http"
)

if ($NoBuild) {
    $dotnetArguments += "--no-build"
}

Push-Location $repoRoot
try {
    $env:ConnectionStrings__DefaultConnection = $connectionString
    & dotnet @dotnetArguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
