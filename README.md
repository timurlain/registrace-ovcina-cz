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
- Azure Container Apps + Azure Container Registry

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

## Azure deployment

This repository now follows the same **general Azure deployment style** as `baca`, but with one important difference:

- `baca` deploys **two images** to **two Azure Container Apps** (`api` + `web`)
- `registrace-ovcina-cz` deploys **one image** to **one Azure Container App** because the Blazor Server app is a single ASP.NET Core application

The `CI` workflow builds and tests every change. On `main` / `master` pushes, or on a manual workflow dispatch, it also:

1. builds the production container image
2. pushes it to Azure Container Registry
3. deploys that image to the target Azure Container App
4. re-applies the production connection string and startup settings

### Required GitHub configuration

Repository **variables**:

| Variable | Purpose |
| --- | --- |
| `AZURE_ACR_NAME` | Azure Container Registry name |
| `AZURE_RESOURCE_GROUP` | Resource group containing the Container App |
| `AZURE_CONTAINER_APP_NAME` | Target Container App name |
| `Email__SharedMailboxAddress` | Exchange Online shared mailbox identity used for app email sending |
| `Email__Graph__TenantId` | Microsoft Entra tenant ID for the mailbox app registration |
| `Email__Graph__ClientId` | Microsoft Entra app registration client ID |

Repository **secrets**:

| Secret | Purpose |
| --- | --- |
| `AZURE_CREDENTIALS` | Service principal JSON used by `azure/login` |
| `AZURE_POSTGRES_CONNECTION_STRING` | Production PostgreSQL connection string |
| `Email__Graph__ClientSecret` | Microsoft Entra app registration client secret |

Mailbox support is enabled only when all four Exchange Online / Microsoft Graph settings are present. If none of them are configured, the app keeps using the current no-op sender. Partial configuration is treated as an error so broken deployments fail fast.

To send from `ovcina@ovcina.cz`, the Microsoft Entra app registration should have Microsoft Graph **application** permissions such as:

- `Mail.Send`
- `Mail.ReadWrite`

The Azure service principal in `AZURE_CREDENTIALS` needs permission to:

- push images to the target ACR
- deploy or update the target Container App
- set Container App secrets and environment variables

### Azure runtime assumptions

- the Azure Container App already exists
- the Container App should be reachable with **external ingress** on port `8080`
- the PostgreSQL server or database already exists
- production startup migrations are enabled through `Database__ApplyMigrationsOnStartup=true`
- demo-user seeding stays disabled in production

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
- Exchange Online shared mailbox sending via Microsoft Graph when configured
- SPAYD QR payments planned and partially scaffolded

## Current status

This repository is intentionally v1-focused. The core registration flow exists, but some deeper behaviors are still planned work, including richer historical person linking, more granular attendee editing, and broader workflow/test coverage.
