using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Announcements;

public sealed class AnnouncementService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Returns the latest active announcement the user hasn't dismissed yet, or null.
    /// </summary>
    public async Task<Announcement?> GetUnseenAnnouncementAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.Announcements
            .AsNoTracking()
            .Where(a => a.IsActive)
            .Where(a => !a.Dismissals.Any(d => d.UserId == userId))
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task DismissAsync(int announcementId, string userId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var alreadyDismissed = await db.AnnouncementDismissals
            .AnyAsync(d => d.AnnouncementId == announcementId && d.UserId == userId, ct);

        if (alreadyDismissed) return;

        db.AnnouncementDismissals.Add(new AnnouncementDismissal
        {
            AnnouncementId = announcementId,
            UserId = userId,
            DismissedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Announcement>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.Announcements
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<int> CreateAsync(string title, string htmlContent, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var announcement = new Announcement
        {
            Title = title,
            HtmlContent = htmlContent,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(ct);
        return announcement.Id;
    }

    public async Task UpdateAsync(int id, string title, string htmlContent, bool isActive, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var announcement = await db.Announcements.FindAsync([id], ct)
            ?? throw new InvalidOperationException("Oznámení nebylo nalezeno.");

        announcement.Title = title;
        announcement.HtmlContent = htmlContent;
        announcement.IsActive = isActive;

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var announcement = await db.Announcements.FindAsync([id], ct);
        if (announcement is null) return;

        db.Announcements.Remove(announcement);
        await db.SaveChangesAsync(ct);
    }
}
