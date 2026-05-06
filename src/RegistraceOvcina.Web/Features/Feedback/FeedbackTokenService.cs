using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Manages per-Registration GUID tokens for the public feedback link path
/// (<c>/zpetna-vazba/{token}</c>). Token is lazily issued on first call to
/// <see cref="EnsureTokenAsync"/>; once issued it never rotates — emails sent
/// out earlier remain valid for the lifetime of the registration.
/// </summary>
/// <remarks>
/// <para><see cref="ResolveAsync"/> looks up by token and refuses to leak the
/// registration after its submission has been soft-deleted. The Registration
/// entity has a global query filter (see <c>ApplicationDbContext</c>) that
/// already excludes registrations whose <see cref="RegistrationSubmission.IsDeleted"/>
/// is true, but the explicit <c>!x.Submission.IsDeleted</c> predicate here is
/// defensive — it carries the test even if the filter evaluation order ever
/// changes for the in-memory provider, and it documents intent at the call site.</para>
/// </remarks>
public sealed class FeedbackTokenService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<Guid> EnsureTokenAsync(int registrationId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var registration = await db.Registrations
            .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Registration {registrationId} not found.");

        if (registration.FeedbackToken is null)
        {
            registration.FeedbackToken = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
        }

        return registration.FeedbackToken.Value;
    }

    public async Task<int?> ResolveAsync(Guid token, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Registrations
            .AsNoTracking()
            .Where(x => x.FeedbackToken == token && !x.Submission.IsDeleted)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
