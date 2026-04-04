# Fresh Codex Start Prompt

Work in this repository:

- `C:\Users\TomášPajonk\source\repos\timurlain\registrace-ovcina-cz`

Use the `registrace-tinkerer` skill if it is available.

Start by reading these files in the repo root:

- `registration-app-starter-prompt.md`
- `registration-app-prompt.md`

Treat `registration-app-starter-prompt.md` as the execution brief and `registration-app-prompt.md` as the authoritative detailed spec.

Project defaults are already decided:

- host in Azure
- use GitHub for CI/CD
- include automated E2E testing from the start
- use a single SQL database
- keep the implementation pragmatic and v1-focused

Technical direction:

- prefer .NET 10 if available, otherwise .NET 8 LTS
- prefer Blazor Server unless the existing repo state justifies something else
- use EF Core with SQL Server / Azure SQL
- use Playwright for E2E coverage
- plan for Exchange Online shared mailbox integration and SPAYD QR payments

Implementation constraints:

- preserve the `RegistrationSubmission` household model
- one submission per game per registrant/contact
- one submission can contain multiple attendees with mixed roles
- login is required before starting a draft
- kingdom assignment is manual in v1
- payment reconciliation is manual in v1
- inbox is in v1 but should stay simple
- the integration API must stay narrow and read-only

What I want you to do now:

1. Inspect the repo and confirm the current state.
2. Scaffold the initial solution and project structure in this repo.
3. Set up the baseline application, database, and test structure.
4. Add GitHub Actions CI/CD scaffolding aimed at Azure deployment.
5. Add Playwright E2E test infrastructure immediately, with at least one smoke test.
6. Implement the first working vertical slice for:
   - create game
   - authenticate
   - create draft household submission
   - add attendees
   - submit
   - generate payment QR
7. Keep the prompt files in the repo root.
8. Explain what you changed, what you verified, and what remains next.

Important working style:

- do not stop at analysis if implementation can begin
- make reasonable assumptions when the prompts already define the product
- challenge any accidental scope creep beyond v1
- add tests as you build, especially E2E coverage for critical flows
- prefer simple, auditable domain modeling over generic shortcuts
