using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Users;

public sealed class UserEmailService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider timeProvider)
{
    public const int MaxAlternateEmails = 4;

    public async Task<List<UserEmail>> GetAlternateEmailsAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.UserEmails
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task AddAlternateEmailAsync(string userId, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ValidationException("E-mail je povinný.");
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new ValidationException("Uživatel nenalezen.");

        if (user.NormalizedEmail == normalizedEmail)
        {
            throw new ValidationException("Alternativní e-mail nesmí být stejný jako primární.");
        }

        var currentCount = await db.UserEmails.CountAsync(x => x.UserId == userId, ct);
        if (currentCount >= MaxAlternateEmails)
        {
            throw new ValidationException("Uživatel může mít maximálně 4 alternativní e-maily.");
        }

        var existsInUsers = await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, ct);
        if (existsInUsers)
        {
            throw new ValidationException("Tento e-mail je již přiřazen jinému účtu.");
        }

        var existsInUserEmails = await db.UserEmails.AnyAsync(x => x.NormalizedEmail == normalizedEmail, ct);
        if (existsInUserEmails)
        {
            throw new ValidationException("Tento e-mail je již přiřazen jinému účtu.");
        }

        db.UserEmails.Add(new UserEmail
        {
            UserId = userId,
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAlternateEmailAsync(string userId, int emailId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var entity = await db.UserEmails
            .SingleOrDefaultAsync(x => x.Id == emailId && x.UserId == userId, ct);

        if (entity is null)
        {
            return;
        }

        db.UserEmails.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var primaryUserId = await db.Users
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(ct);

        if (primaryUserId is not null)
        {
            return primaryUserId;
        }

        return await db.UserEmails
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => x.UserId)
            .SingleOrDefaultAsync(ct);
    }
}
