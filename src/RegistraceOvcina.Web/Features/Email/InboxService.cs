using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Email;

public sealed class InboxService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<InboxPageResult> GetMessagesAsync(int page = 1, int pageSize = 25)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var query = db.EmailMessages
            .Include(e => e.LinkedSubmission)
            .Include(e => e.LinkedPerson)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .AsNoTracking();

        var totalCount = await query.CountAsync();
        var messages = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new InboxPageResult(messages, totalCount, page, pageSize);
    }

    public async Task<EmailMessage?> GetMessageAsync(int id)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        return await db.EmailMessages
            .Include(e => e.LinkedSubmission)
            .Include(e => e.LinkedPerson)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task LinkToSubmissionAsync(int messageId, int submissionId, string actorUserId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var message = await db.EmailMessages.FindAsync(messageId);
        if (message is null) return;

        message.LinkedSubmissionId = submissionId;
        message.LinkedPersonId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = messageId.ToString(),
            Action = "LinkToSubmission",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = $"{{\"submissionId\":{submissionId}}}"
        });

        await db.SaveChangesAsync();
    }

    public async Task LinkToPersonAsync(int messageId, int personId, string actorUserId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var message = await db.EmailMessages.FindAsync(messageId);
        if (message is null) return;

        message.LinkedPersonId = personId;
        message.LinkedSubmissionId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = messageId.ToString(),
            Action = "LinkToPerson",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = $"{{\"personId\":{personId}}}"
        });

        await db.SaveChangesAsync();
    }

    public async Task UnlinkAsync(int messageId, string actorUserId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var message = await db.EmailMessages.FindAsync(messageId);
        if (message is null) return;

        message.LinkedSubmissionId = null;
        message.LinkedPersonId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = messageId.ToString(),
            Action = "Unlink",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task<List<SubmissionLookupItem>> GetSubmissionLookupAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        return await db.RegistrationSubmissions
            .Where(s => s.Status == SubmissionStatus.Submitted)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Select(s => new SubmissionLookupItem(
                s.Id,
                $"#{s.Id} — {s.PrimaryContactName} ({s.Game.Name})"))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<PersonLookupItem>> GetPersonLookupAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        return await db.People
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Select(p => new PersonLookupItem(
                p.Id,
                $"{p.LastName} {p.FirstName}"))
            .AsNoTracking()
            .ToListAsync();
    }
}

public sealed record InboxPageResult(
    List<EmailMessage> Messages,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public sealed record SubmissionLookupItem(int Id, string Label);
public sealed record PersonLookupItem(int Id, string Name);
