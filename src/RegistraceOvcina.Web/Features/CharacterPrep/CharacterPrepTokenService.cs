using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.CharacterPrep;

public sealed class CharacterPrepTokenService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<string> EnsureTokenAsync(int submissionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegistrationSubmission {submissionId} not found.");

        if (!string.IsNullOrEmpty(submission.CharacterPrepToken))
        {
            return submission.CharacterPrepToken;
        }

        submission.CharacterPrepToken = GenerateToken();
        await db.SaveChangesAsync(cancellationToken);
        return submission.CharacterPrepToken;
    }

    public async Task<string> RotateTokenAsync(int submissionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegistrationSubmission {submissionId} not found.");

        submission.CharacterPrepToken = GenerateToken();
        await db.SaveChangesAsync(cancellationToken);
        return submission.CharacterPrepToken;
    }

    public async Task<RegistrationSubmission?> FindBySubmissionTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.CharacterPrepToken == token, cancellationToken);
    }

    private static string GenerateToken() =>
        Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
