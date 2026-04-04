[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$containerName = "registrace-ovcina-db"
$databaseName = "registrace_ovcina_development"
$databasePort = 5433

function Test-LocalPortListening {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    return $null -ne (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Wait-ForDockerPostgres {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string]$DatabaseName
    )

    $deadline = (Get-Date).AddSeconds(60)

    while ((Get-Date) -lt $deadline) {
        docker exec $ContainerName pg_isready --host localhost --username postgres --dbname $DatabaseName | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for PostgreSQL container '$ContainerName' to become ready."
}

if (Test-LocalPortListening -Port $databasePort) {
    Write-Host "PostgreSQL is already listening on port $databasePort." -ForegroundColor Green
    return
}

if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "PostgreSQL is not listening on port $databasePort and Docker is not available."
}

$existingContainer = docker ps -a --format "{{.Names}}" | Where-Object { $_ -eq $containerName }

if ($existingContainer) {
    Write-Host "Starting existing PostgreSQL container '$containerName' on port $databasePort..." -ForegroundColor Cyan
    docker start $containerName | Out-Null
}
else {
    Write-Host "Creating PostgreSQL container '$containerName' on port $databasePort..." -ForegroundColor Cyan
    docker run --name $containerName -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=$databaseName -p "${databasePort}:5432" -d postgres:16-alpine | Out-Null
}

Wait-ForDockerPostgres -ContainerName $containerName -DatabaseName $databaseName
Write-Host "PostgreSQL is ready on port $databasePort." -ForegroundColor Green
