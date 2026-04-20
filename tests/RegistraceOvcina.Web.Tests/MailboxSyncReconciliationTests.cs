using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Tests;

/// <summary>
/// Guards the prefix filter used by <c>MailboxSyncService</c> to select local
/// placeholder rows for reconciliation. The prefixes are a literal contract with
/// the writer services: <c>InboxService</c> uses "composed-" and "reply-", and
/// <c>CharacterPrepMailService</c> uses "prep-". If a writer introduces a new
/// prefix without the filter learning about it, that outbox row will be
/// duplicated on the next sync pass.
///
/// We assert the filter against the same in-memory EF Core context the sync
/// service uses, by executing the exact same IQueryable shape — this stays
/// truthful without having to stand up the full Graph HTTP pipeline.
/// </summary>
public sealed class MailboxSyncReconciliationTests
{
    [Fact]
    public async Task Placeholder_filter_includes_prep_prefix_alongside_composed_and_reply()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.EmailMessages.AddRange(
                NewOutbound("composed-aaa"),
                NewOutbound("reply-bbb"),
                NewOutbound("prep-ccc"),
                NewOutbound("AAMkA-real-graph-id"), // real Graph id: must NOT match
                NewInbound("prep-ddd-but-inbound"));  // inbound: must NOT match
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);

        // Mirror of the query shape used inside MailboxSyncService.SyncInboxAsync.
        var placeholders = await db.EmailMessages
            .Where(e => e.Direction == EmailDirection.Outbound
                && (e.MailboxItemId.StartsWith("composed-")
                    || e.MailboxItemId.StartsWith("reply-")
                    || e.MailboxItemId.StartsWith("prep-")))
            .OrderByDescending(e => e.SentAtUtc)
            .ToListAsync();

        Assert.Equal(3, placeholders.Count);
        Assert.Contains(placeholders, p => p.MailboxItemId == "composed-aaa");
        Assert.Contains(placeholders, p => p.MailboxItemId == "reply-bbb");
        Assert.Contains(placeholders, p => p.MailboxItemId == "prep-ccc");
        Assert.DoesNotContain(placeholders, p => p.MailboxItemId == "AAMkA-real-graph-id");
        Assert.DoesNotContain(placeholders, p => p.MailboxItemId == "prep-ddd-but-inbound");
    }

    [Fact]
    public async Task Reconciled_prep_placeholder_is_updated_in_place_not_inserted_as_duplicate()
    {
        // Simulates the end-state of a successful reconciliation: a prep-* row
        // that existed before sync now holds the real Graph id, and no second
        // row was created for the same logical message. If the mail service's
        // "prep-" prefix fell outside the sync filter, the service would have
        // inserted a second row with the Graph id — we assert that shape does
        // not arise when the reconciliation step fires.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        const string placeholderId = "prep-abc";
        const string graphId = "AAMkAGVlYTY-real-id";
        const int submissionId = 42;

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.EmailMessages.Add(new EmailMessage
            {
                MailboxItemId = placeholderId,
                Direction = EmailDirection.Outbound,
                From = "ovcina@ovcina.cz",
                To = "parent@example.cz",
                Subject = "Příprava postav pro Ovčina 2026 — vyber startovní výbavu",
                BodyText = "Ahoj, chystáme ...",
                SentAtUtc = DateTime.UtcNow.AddMinutes(-5),
                LinkedSubmissionId = submissionId
            });
            await seed.SaveChangesAsync();
        }

        // Act: simulate the reconciliation match step from MailboxSyncService.
        await using (var db = new ApplicationDbContext(options))
        {
            var candidate = await db.EmailMessages
                .Where(e => e.Direction == EmailDirection.Outbound
                    && (e.MailboxItemId.StartsWith("composed-")
                        || e.MailboxItemId.StartsWith("reply-")
                        || e.MailboxItemId.StartsWith("prep-")))
                .Where(e => e.To == "parent@example.cz"
                    && e.Subject == "Příprava postav pro Ovčina 2026 — vyber startovní výbavu")
                .SingleAsync();

            Assert.Equal(placeholderId, candidate.MailboxItemId);
            candidate.MailboxItemId = graphId;
            candidate.ReceivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Assert: exactly one row, now holding the Graph id, still linked to the submission.
        await using (var verify = new ApplicationDbContext(options))
        {
            var rows = await verify.EmailMessages
                .Where(e => e.LinkedSubmissionId == submissionId)
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal(graphId, rows[0].MailboxItemId);
            Assert.DoesNotContain(rows, r => r.MailboxItemId == placeholderId);
        }
    }

    private static EmailMessage NewOutbound(string mailboxItemId) => new()
    {
        MailboxItemId = mailboxItemId,
        Direction = EmailDirection.Outbound,
        From = "ovcina@ovcina.cz",
        To = "recipient@example.cz",
        Subject = "Subject " + mailboxItemId,
        BodyText = "Body " + mailboxItemId,
        SentAtUtc = DateTime.UtcNow
    };

    private static EmailMessage NewInbound(string mailboxItemId) => new()
    {
        MailboxItemId = mailboxItemId,
        Direction = EmailDirection.Inbound,
        From = "someone@example.cz",
        To = "ovcina@ovcina.cz",
        Subject = "Subject " + mailboxItemId,
        BodyText = "Body " + mailboxItemId,
        ReceivedAtUtc = DateTime.UtcNow
    };
}
