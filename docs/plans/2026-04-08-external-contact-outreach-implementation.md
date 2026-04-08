# External Contact Outreach ("Oslovení") Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Admin page to manage external contacts (emails) and send them bulk emails via Microsoft Graph.

**Architecture:** New `ExternalContact` entity + `ExternalContactService` + Blazor admin page at `/admin/osloveni`. Sends via existing Graph shared mailbox infrastructure (same as InvitationService).

**Tech Stack:** .NET 10, Blazor Server, EF Core, Microsoft Graph HTTP API

---

### Task 1: Data Model — ExternalContact entity

**Files:**
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationModels.cs`
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs`

**Step 1: Add ExternalContact class to ApplicationModels.cs**

At end of file, add:

```csharp
public class ExternalContact
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
```

**Step 2: Add DbSet and configuration to ApplicationDbContext.cs**

Add DbSet alongside existing ones:

```csharp
public DbSet<ExternalContact> ExternalContacts => Set<ExternalContact>();
```

Add to `OnModelCreating`:

```csharp
builder.Entity<ExternalContact>(entity =>
{
    entity.HasKey(x => x.Id);
    entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
    entity.HasIndex(x => x.Email).IsUnique();
});
```

**Step 3: Generate migration**

```bash
cd src/RegistraceOvcina.Web
dotnet ef migrations add AddExternalContact
```

Verify the migration file contains: CreateTable with Id, Email (varchar 320), CreatedAtUtc, and a unique index on Email.

**Step 4: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 2: ExternalContactService

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/ExternalContacts/ExternalContactService.cs`

**Step 1: Create the service**

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;

namespace RegistraceOvcina.Web.Features.ExternalContacts;

public sealed class ExternalContactService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IGraphAccessTokenProvider accessTokenProvider,
    IOptions<MailboxEmailOptions> emailOptions,
    ILogger<ExternalContactService> logger,
    TimeProvider timeProvider)
{
    public async Task<(int Added, int Skipped)> ImportAsync(
        IEnumerable<string> emails,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var added = 0;
        var skipped = 0;

        foreach (var raw in emails)
        {
            var email = raw.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                skipped++;
                continue;
            }

            var exists = await db.ExternalContacts
                .AnyAsync(x => x.Email == email, cancellationToken);

            if (exists)
            {
                skipped++;
                continue;
            }

            db.ExternalContacts.Add(new ExternalContact
            {
                Email = email,
                CreatedAtUtc = nowUtc
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (added, skipped);
    }

    public async Task<List<ExternalContact>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExternalContacts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.ExternalContacts
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> SendToAllAsync(
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var contacts = await db.ExternalContacts
            .AsNoTracking()
            .Select(x => x.Email)
            .ToListAsync(cancellationToken);

        if (contacts.Count == 0) return 0;

        var opts = emailOptions.Value;
        if (!opts.IsConfigured)
        {
            logger.LogWarning("Mailbox not configured — skipping send to {Count} contacts", contacts.Count);
            return 0;
        }

        var token = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        using var client = httpClientFactory.CreateClient("MicrosoftGraph");
        var sent = 0;

        foreach (var email in contacts)
        {
            try
            {
                await SendViaGraphAsync(client, token, opts.SharedMailboxAddress, email, subject, htmlBody, cancellationToken);
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send outreach email to {Email}", email);
            }
        }

        logger.LogInformation("Outreach email sent to {Sent}/{Total} contacts", sent, contacts.Count);
        return sent;
    }

    private static async Task SendViaGraphAsync(
        HttpClient client,
        string accessToken,
        string fromMailbox,
        string recipientAddress,
        string subject,
        string htmlContent,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{Uri.EscapeDataString(fromMailbox)}/sendMail");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new { contentType = "HTML", content = htmlContent },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = recipientAddress } }
                }
            },
            saveToSentItems = true
        });

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Microsoft Graph sendMail failed ({(int)response.StatusCode}): {body}");
        }
    }
}
```

**Step 2: Register in DI**

In `Program.cs`, find where other services are registered (e.g. `InvitationService`) and add:

```csharp
builder.Services.AddScoped<ExternalContactService>();
```

**Step 3: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 3: Admin Page — Oslovení

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Admin/ExternalContacts.razor`
- Modify: `src/RegistraceOvcina.Web/Components/Layout/MainLayout.razor`

**Step 1: Create the Razor page**

```razor
@page "/admin/osloveni"
@attribute [Authorize(Policy = AuthorizationPolicies.AdminOnly)]

@using RegistraceOvcina.Web.Data
@using RegistraceOvcina.Web.Features.ExternalContacts
@using RegistraceOvcina.Web.Security

@inject ExternalContactService ContactService

@rendermode InteractiveServer

<PageTitle>Oslovení</PageTitle>

<div class="row g-4">
    <div class="col-12">
        <section class="card shadow-sm">
            <div class="card-body p-4">
                <h1 class="h3 mb-4">Oslovení — externí kontakty</h1>

                @if (!string.IsNullOrEmpty(statusMessage))
                {
                    <div class="alert @statusCss alert-dismissible fade show" role="alert">
                        @statusMessage
                        <button type="button" class="btn-close" @onclick="() => statusMessage = null"></button>
                    </div>
                }

                <div class="row g-3 mb-4">
                    <div class="col-md-6">
                        <h2 class="h5">Přidat e-mail</h2>
                        <div class="input-group">
                            <input type="email" class="form-control" placeholder="email@example.com"
                                   @bind="singleEmail" @bind:event="oninput" data-testid="single-email" />
                            <button class="btn btn-outline-primary" @onclick="AddSingleAsync"
                                    disabled="@string.IsNullOrWhiteSpace(singleEmail)" data-testid="add-single">
                                Přidat
                            </button>
                        </div>
                    </div>
                    <div class="col-md-6">
                        <h2 class="h5">Importovat seznam</h2>
                        <textarea class="form-control mb-2" rows="3" placeholder="Jeden e-mail na řádek"
                                  @bind="bulkEmails" data-testid="bulk-emails"></textarea>
                        <button class="btn btn-outline-primary" @onclick="ImportBulkAsync"
                                disabled="@string.IsNullOrWhiteSpace(bulkEmails)" data-testid="import-bulk">
                            Importovat
                        </button>
                    </div>
                </div>

                <h2 class="h5">Kontakty (@contacts.Count)</h2>
                @if (contacts.Count == 0)
                {
                    <p class="text-secondary">Zatím žádné kontakty.</p>
                }
                else
                {
                    <div class="table-responsive mb-4">
                        <table class="table table-sm align-middle">
                            <thead>
                                <tr>
                                    <th>E-mail</th>
                                    <th>Přidáno</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                                @foreach (var c in contacts)
                                {
                                    <tr>
                                        <td>@c.Email</td>
                                        <td>@c.CreatedAtUtc.ToString("d.M.yyyy")</td>
                                        <td class="text-end">
                                            <button class="btn btn-sm btn-outline-danger"
                                                    @onclick="() => DeleteAsync(c.Id)">Smazat</button>
                                        </td>
                                    </tr>
                                }
                            </tbody>
                        </table>
                    </div>
                }

                <h2 class="h5 mt-4">Odeslat e-mail</h2>
                <div class="mb-3">
                    <label class="form-label" for="subject">Předmět</label>
                    <input id="subject" class="form-control" @bind="emailSubject" data-testid="email-subject" />
                </div>
                <div class="mb-3">
                    <label class="form-label" for="body">Text (HTML)</label>
                    <textarea id="body" class="form-control" rows="8" @bind="emailBody" data-testid="email-body"></textarea>
                </div>
                <button class="btn btn-primary" @onclick="ConfirmSend"
                        disabled="@(contacts.Count == 0 || string.IsNullOrWhiteSpace(emailSubject) || string.IsNullOrWhiteSpace(emailBody))"
                        data-testid="send-all">
                    Odeslat všem (@contacts.Count)
                </button>

                @if (showConfirm)
                {
                    <div class="modal d-block" tabindex="-1" style="background:rgba(0,0,0,.5)">
                        <div class="modal-dialog modal-dialog-centered">
                            <div class="modal-content">
                                <div class="modal-header">
                                    <h5 class="modal-title">Potvrdit odeslání</h5>
                                </div>
                                <div class="modal-body">
                                    <p>Opravdu odeslat e-mail <strong>@contacts.Count</strong> kontaktům?</p>
                                </div>
                                <div class="modal-footer">
                                    <button class="btn btn-secondary" @onclick="() => showConfirm = false">Zrušit</button>
                                    <button class="btn btn-primary" @onclick="SendAllAsync" disabled="@isSending"
                                            data-testid="confirm-send">
                                        @(isSending ? "Odesílám..." : "Odeslat")
                                    </button>
                                </div>
                            </div>
                        </div>
                    </div>
                }
            </div>
        </section>
    </div>
</div>

@code {
    private List<ExternalContact> contacts = [];
    private string? singleEmail;
    private string? bulkEmails;
    private string? emailSubject;
    private string? emailBody;
    private string? statusMessage;
    private string statusCss = "alert-info";
    private bool showConfirm;
    private bool isSending;

    protected override async Task OnInitializedAsync()
    {
        contacts = await ContactService.GetAllAsync();
    }

    private async Task AddSingleAsync()
    {
        if (string.IsNullOrWhiteSpace(singleEmail)) return;
        var (added, skipped) = await ContactService.ImportAsync([singleEmail]);
        singleEmail = null;
        contacts = await ContactService.GetAllAsync();
        SetStatus(added > 0 ? "Kontakt přidán." : "E-mail už existuje.", added > 0);
    }

    private async Task ImportBulkAsync()
    {
        if (string.IsNullOrWhiteSpace(bulkEmails)) return;
        var lines = bulkEmails.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (added, skipped) = await ContactService.ImportAsync(lines);
        bulkEmails = null;
        contacts = await ContactService.GetAllAsync();
        SetStatus($"Přidáno: {added}, přeskočeno: {skipped}.", added > 0);
    }

    private async Task DeleteAsync(int id)
    {
        await ContactService.DeleteAsync(id);
        contacts = await ContactService.GetAllAsync();
        SetStatus("Kontakt smazán.", true);
    }

    private void ConfirmSend() => showConfirm = true;

    private async Task SendAllAsync()
    {
        isSending = true;
        showConfirm = false;
        try
        {
            var sent = await ContactService.SendToAllAsync(emailSubject!, emailBody!);
            SetStatus($"E-mail odeslán {sent} kontaktům.", true);
            emailSubject = null;
            emailBody = null;
        }
        catch (Exception)
        {
            SetStatus("Chyba při odesílání e-mailu.", false);
        }
        finally
        {
            isSending = false;
        }
    }

    private void SetStatus(string message, bool success)
    {
        statusMessage = message;
        statusCss = success ? "alert-success" : "alert-warning";
    }
}
```

**Step 2: Add nav link to MainLayout.razor**

In the Admin `<AuthorizeView>` block (after the "Oznámení" link), add:

```html
<a class="nav-link" href="/admin/osloveni">Oslovení</a>
```

**Step 3: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 4: Final verification

**Step 1: Run full build**

```bash
dotnet build
```

**Step 2: Run E2E tests (existing)**

```bash
dotnet test tests/RegistraceOvcina.E2E/RegistraceOvcina.E2E.csproj
```

Ensure no regressions.

**Step 3: Verify no pending model changes**

```bash
cd src/RegistraceOvcina.Web
dotnet ef migrations has-pending-model-changes
```

Expected: "No pending model changes."
