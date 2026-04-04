# Registrace Ovčina

Pragmatic v1 registration app for the Ovčina LARP event.

The app is built as a single .NET 10 Blazor Server application with PostgreSQL, ASP.NET Core Identity, GitHub Actions, and Playwright E2E coverage. It currently covers the first end-to-end slice for:

- game creation
- account registration and login
- draft submission creation
- attendee add/remove
- submission
- SPAYD payment QR generation

## Stack

- .NET 10
- Blazor Server / Razor Components
- PostgreSQL + EF Core + Npgsql
- ASP.NET Core Identity
- xUnit
- Playwright + Testcontainers
- GitHub Actions

## Quick start

### 1. Prerequisites

- .NET SDK 10
- Docker Desktop
- PowerShell

Optional for Visual Studio / HTTPS:

```powershell
dotnet dev-certs https --trust
```

If you do not want to use HTTPS locally, use the HTTP profile or the provided `run-app.ps1` script.

### 2. Start the local database

```powershell
.\start-db.ps1
```

This starts a dedicated Docker PostgreSQL container on port `5433` so it does not collide with an existing local PostgreSQL instance on `5432`.

### 3. Run the app

Recommended:

```powershell
.\run-app.ps1
```

That script ensures the Docker database is available and starts the app with the local development connection string.

Windows batch alternative that builds the solution, starts the app, and opens Chrome:

```bat
run-app.bat
```

That launcher also stops the currently running local app instance first, so it can rebuild and restart cleanly.

Manual alternative:

```powershell
dotnet run --project .\src\RegistraceOvcina.Web\RegistraceOvcina.Web.csproj --launch-profile http
```

### 4. Seeded local users

The development database initializer creates these users:

| Role | Email | Password |
| --- | --- | --- |
| Admin | `admin@ovcina.test` | `Pass123!` |
| Registrant | `registrant@ovcina.test` | `Pass123!` |

## Tests

Unit tests:

```powershell
dotnet test .\tests\RegistraceOvcina.Web.Tests\RegistraceOvcina.Web.Tests.csproj
```

E2E tests:

```powershell
dotnet test .\tests\RegistraceOvcina.E2E\RegistraceOvcina.E2E.csproj
```

The E2E suite starts its own disposable PostgreSQL container via Testcontainers.

## Repository layout

```text
src/RegistraceOvcina.Web      Main web app
tests/RegistraceOvcina.Web.Tests  Unit tests
tests/RegistraceOvcina.E2E    End-to-end tests
.github/workflows             CI/CD and Azure deployment scaffolding
registration-app-prompt.md    Product spec
registration-app-starter-prompt.md  Execution brief
```

## Deployment direction

- Azure hosting
- GitHub Actions for CI/CD
- PostgreSQL as the application database
- narrow read-only integration API
- Exchange Online shared mailbox integration planned
- SPAYD QR payments planned and partially scaffolded

## Current status

This repository is intentionally v1-focused. The core registration flow exists, but some deeper behaviors are still planned work, including richer historical person linking, more granular attendee editing, and broader workflow/test coverage.
