# Session Handoff

> Generated: 2026-04-08 (reconstructed from crashed session ~2026-04-06) | Branch: fix/computed-totals | Last commit: b668d88

## Completed

- [x] **fix/computed-totals** — committed and pushed: always compute expected total from registrations, allow donation after submission
- [x] **E2E Smoke Tests** written (`tests/RegistraceOvcina.E2E/SmokeTests.cs`)
  - 7 Playwright tests: public links, simple password registration, admin game management, role management (organizer/admin add/remove), historical Excel import, food summary aggregation, full admin+registrant end-to-end flow
  - Uses `AppFixture` (Testcontainers), `data-testid` selectors, `WaitForInteractiveReadyAsync` helper, `AssertNoBlazorErrorsAsync` guard
  - Proper failure diagnostics: captures page body + host diagnostics on `TimeoutException`
- [x] **CI workflow update** (`.github/workflows/ci.yml`) — pass `AZURECOMMUNICATION__CONNECTIONSTRING` as container app secret during deploy
- [x] **Singleton DI fix** — field-cached clients need `AddSingleton`, not `AddScoped` (captured in MEMORY.md)

## Pending

- [ ] **Run E2E tests** — `SmokeTests.cs` is written but not verified passing
  - Needs Playwright browsers installed: `pwsh bin/Debug/net10.0/playwright.ps1 install`
  - May need `data-testid` attributes added to Blazor components if not already present
- [ ] **Commit uncommitted changes** — two unstaged files:
  - `.github/workflows/ci.yml` (ACS connection string secret)
  - `tests/RegistraceOvcina.E2E/SmokeTests.cs` (full E2E suite)
- [ ] **Push main to origin** — `main` is 1 commit ahead of `origin/main` (commit `b668d88`)
- [ ] **Merge fix/computed-totals to main** if not already merged (both point to same commit `b668d88`)
- [ ] **E2E test project setup** — verify `RegistraceOvcina.E2E.csproj` exists with Playwright + ClosedXML + xUnit deps, and `AppFixture.cs` is wired up

## Learned

- Session crashed before handoff could be written — no `.claude/handoff.md` existed in this repo until now
- E2E tests use ClosedXML to generate Excel workbooks for historical import testing (in-memory workbook creation)
- Food summary test seeds data via `SeedFoodSummaryAsync()` helper and verifies aggregated counts by day+option

## Context

- **Repo**: `C:\Users\TomášPajonk\source\repos\timurlain\registrace-ovcina-cz`
- **GitHub**: `timurlain/registrace-ovcina-cz` (private)
- **Local dev**: `devStart.bat` (docker + build + API + Client)
- **Tests**: existing test suite + new E2E smoke tests (uncommitted)
- **Skill**: `registrace-ovcina-tinkerer` — project-specific implementation skill
