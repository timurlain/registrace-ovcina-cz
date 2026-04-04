using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Submissions;

public sealed class SubmissionService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    SubmissionPricingService pricingService,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<SubmissionSummary>> GetUserSubmissionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submissions = await db.RegistrationSubmissions
            .AsNoTracking()
            .Where(x => x.RegistrantUserId == userId)
            .OrderByDescending(x => x.LastEditedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.GameId,
                GameName = x.Game.Name,
                x.Status,
                x.SubmittedAtUtc,
                x.ExpectedTotalAmount,
                PaidAmount = x.Payments.Sum(p => p.Amount)
            })
            .ToListAsync(cancellationToken);

        return submissions
            .Select(x => new SubmissionSummary(
                x.Id,
                x.GameId,
                x.GameName,
                x.Status,
                x.SubmittedAtUtc,
                x.ExpectedTotalAmount,
                pricingService.CalculateBalanceStatus(x.ExpectedTotalAmount, x.PaidAmount)))
            .ToList();
    }

    public async Task<int> CreateOrResumeDraftAsync(
        int gameId,
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existingSubmissionId = await db.RegistrationSubmissions
            .Where(x => x.GameId == gameId && x.RegistrantUserId == user.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingSubmissionId.HasValue)
        {
            return existingSubmissionId.Value;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var game = await db.Games.FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken)
            ?? throw new ValidationException("Vybraná hra neexistuje.");

        if (!game.IsPublished)
        {
            throw new ValidationException("Tuto hru zatím nelze přihlašovat.");
        }

        if (game.RegistrationClosesAtUtc < nowUtc)
        {
            throw new ValidationException("Registrace pro tuto hru už byla uzavřena.");
        }

        var submission = new RegistrationSubmission
        {
            GameId = gameId,
            RegistrantUserId = user.Id,
            PrimaryContactName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? user.UserName ?? "" : user.DisplayName,
            PrimaryEmail = user.Email ?? "",
            PrimaryPhone = user.PhoneNumber ?? "",
            Status = SubmissionStatus.Draft,
            LastEditedAtUtc = nowUtc,
            ExpectedTotalAmount = 0m
        };

        db.RegistrationSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(RegistrationSubmission),
            EntityId = submission.Id.ToString(),
            Action = "SubmissionDraftCreated",
            ActorUserId = user.Id,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new { submission.GameId })
        });

        await db.SaveChangesAsync(cancellationToken);
        return submission.Id;
    }

    public async Task<SubmissionEditorViewModel?> GetSubmissionAsync(
        int submissionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .Include(x => x.Game)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.Person)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.PreferredKingdom)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken);

        if (submission is null)
        {
            return null;
        }

        var currentTotal = pricingService.CalculateExpectedTotal(submission.Game, submission.Registrations);
        var paidAmount = submission.Payments.Sum(x => x.Amount);

        var kingdoms = await db.Kingdoms
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => new LookupItem(x.Id, x.DisplayName))
            .ToListAsync(cancellationToken);

        return new SubmissionEditorViewModel
        {
            Id = submission.Id,
            GameId = submission.GameId,
            GameName = submission.Game.Name,
            GameStartsAtUtc = submission.Game.StartsAtUtc,
            RegistrationClosesAtUtc = submission.Game.RegistrationClosesAtUtc,
            PaymentDueAtUtc = submission.Game.PaymentDueAtUtc,
            BankAccount = submission.Game.BankAccount,
            BankAccountName = submission.Game.BankAccountName,
            Status = submission.Status,
            SubmittedAtUtc = submission.SubmittedAtUtc,
            PrimaryContactName = submission.PrimaryContactName,
            PrimaryEmail = submission.PrimaryEmail,
            PrimaryPhone = submission.PrimaryPhone,
            RegistrantNote = submission.RegistrantNote,
            CurrentTotalAmount = currentTotal,
            PaidAmount = paidAmount,
            ExpectedTotalAmount = submission.Status == SubmissionStatus.Submitted ? submission.ExpectedTotalAmount : currentTotal,
            PaymentVariableSymbol = submission.PaymentVariableSymbol,
            BalanceStatus = pricingService.CalculateBalanceStatus(
                submission.Status == SubmissionStatus.Submitted ? submission.ExpectedTotalAmount : currentTotal,
                paidAmount),
            AvailableKingdoms = kingdoms,
            Attendees = submission.Registrations
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new AttendeeViewModel(
                    x.Id,
                    $"{x.Person.FirstName} {x.Person.LastName}",
                    x.Person.BirthYear,
                    x.Role,
                    x.PreferredKingdom?.DisplayName,
                    x.GuardianAuthorizationConfirmed,
                    x.ContactEmail,
                    x.ContactPhone))
                .ToList()
        };
    }

    public async Task UpdateContactAsync(
        int submissionId,
        string userId,
        ContactInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await LoadOwnedSubmissionAsync(db, submissionId, userId, cancellationToken);
        EnsureDraft(submission);

        submission.PrimaryContactName = input.PrimaryContactName.Trim();
        submission.PrimaryEmail = input.PrimaryEmail.Trim();
        submission.PrimaryPhone = input.PrimaryPhone.Trim();
        submission.RegistrantNote = string.IsNullOrWhiteSpace(input.RegistrantNote) ? null : input.RegistrantNote.Trim();
        submission.LastEditedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAttendeeAsync(
        int submissionId,
        string userId,
        AttendeeInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await db.RegistrationSubmissions
            .Include(x => x.Game)
            .Include(x => x.Registrations)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");

        EnsureDraft(submission);

        if (pricingService.RequiresGuardianData(input.BirthYear))
        {
            if (string.IsNullOrWhiteSpace(input.GuardianName) || string.IsNullOrWhiteSpace(input.GuardianRelationship) || !input.GuardianAuthorizationConfirmed)
            {
                throw new ValidationException("Nezletilý účastník musí mít vyplněného zákonného zástupce a potvrzení souhlasu.");
            }
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var person = new Person
        {
            FirstName = input.FirstName.Trim(),
            LastName = input.LastName.Trim(),
            BirthYear = input.BirthYear,
            Email = string.IsNullOrWhiteSpace(input.ContactEmail) ? null : input.ContactEmail.Trim(),
            Phone = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var registration = new Registration
        {
            Person = person,
            SubmissionId = submission.Id,
            Role = input.Role,
            Status = RegistrationStatus.Active,
            PreferredKingdomId = input.Role == RegistrationRole.Player ? input.PreferredKingdomId : null,
            ContactEmail = string.IsNullOrWhiteSpace(input.ContactEmail) ? null : input.ContactEmail.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim(),
            GuardianName = string.IsNullOrWhiteSpace(input.GuardianName) ? null : input.GuardianName.Trim(),
            GuardianRelationship = string.IsNullOrWhiteSpace(input.GuardianRelationship) ? null : input.GuardianRelationship.Trim(),
            GuardianAuthorizationConfirmed = input.GuardianAuthorizationConfirmed,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.Registrations.Add(registration);
        submission.LastEditedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Registration),
            EntityId = registration.Id.ToString(),
            Action = "AttendeeAdded",
            ActorUserId = userId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                registration.SubmissionId,
                person.FirstName,
                person.LastName,
                registration.Role
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAttendeeAsync(
        int submissionId,
        int registrationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await LoadOwnedSubmissionAsync(db, submissionId, userId, cancellationToken);
        EnsureDraft(submission);

        var registration = await db.Registrations
            .Include(x => x.Person)
            .FirstOrDefaultAsync(x => x.Id == registrationId && x.SubmissionId == submissionId, cancellationToken)
            ?? throw new ValidationException("Účastník nebyl nalezen.");

        var personId = registration.PersonId;
        db.Registrations.Remove(registration);

        var hasOtherRegistrations = await db.Registrations
            .AnyAsync(x => x.PersonId == personId && x.Id != registrationId, cancellationToken);

        if (!hasOtherRegistrations && registration.Person is not null)
        {
            db.People.Remove(registration.Person);
        }

        submission.LastEditedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitAsync(
        int submissionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await db.RegistrationSubmissions
            .Include(x => x.Game)
            .Include(x => x.Registrations)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");

        EnsureDraft(submission);

        if (submission.Game.RegistrationClosesAtUtc < timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new ValidationException("Registrace pro tuto hru je už uzavřená.");
        }

        if (submission.Registrations.Count == 0)
        {
            throw new ValidationException("Před odesláním je potřeba přidat alespoň jednoho účastníka.");
        }

        if (string.IsNullOrWhiteSpace(submission.PrimaryContactName)
            || string.IsNullOrWhiteSpace(submission.PrimaryEmail)
            || string.IsNullOrWhiteSpace(submission.PrimaryPhone))
        {
            throw new ValidationException("Doplňte prosím kontaktní údaje skupiny nebo rodiny.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        submission.ExpectedTotalAmount = pricingService.CalculateExpectedTotal(submission.Game, submission.Registrations);
        submission.Status = SubmissionStatus.Submitted;
        submission.SubmittedAtUtc = nowUtc;
        submission.LastEditedAtUtc = nowUtc;
        submission.PaymentVariableSymbol ??= submission.Id.ToString("D10");

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(RegistrationSubmission),
            EntityId = submission.Id.ToString(),
            Action = "SubmissionSubmitted",
            ActorUserId = userId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                submission.ExpectedTotalAmount,
                AttendeeCount = submission.Registrations.Count
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureDraft(RegistrationSubmission submission)
    {
        if (submission.Status != SubmissionStatus.Draft)
        {
            throw new ValidationException("Tuto přihlášku už nelze upravovat v rozpracovaném režimu.");
        }
    }

    private static async Task<RegistrationSubmission> LoadOwnedSubmissionAsync(
        ApplicationDbContext db,
        int submissionId,
        string userId,
        CancellationToken cancellationToken)
    {
        return await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");
    }
}

public sealed record LookupItem(int Id, string Name);

public sealed record SubmissionSummary(
    int Id,
    int GameId,
    string GameName,
    SubmissionStatus Status,
    DateTime? SubmittedAtUtc,
    decimal ExpectedTotalAmount,
    BalanceStatus BalanceStatus);

public sealed record AttendeeViewModel(
    int RegistrationId,
    string FullName,
    int BirthYear,
    RegistrationRole Role,
    string? PreferredKingdom,
    bool GuardianAuthorizationConfirmed,
    string? ContactEmail,
    string? ContactPhone);

public sealed class SubmissionEditorViewModel
{
    public int Id { get; init; }
    public int GameId { get; init; }
    public string GameName { get; init; } = "";
    public DateTime GameStartsAtUtc { get; init; }
    public DateTime RegistrationClosesAtUtc { get; init; }
    public DateTime PaymentDueAtUtc { get; init; }
    public string BankAccount { get; init; } = "";
    public string BankAccountName { get; init; } = "";
    public SubmissionStatus Status { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public string PrimaryContactName { get; init; } = "";
    public string PrimaryEmail { get; init; } = "";
    public string PrimaryPhone { get; init; } = "";
    public string? RegistrantNote { get; init; }
    public decimal CurrentTotalAmount { get; init; }
    public decimal ExpectedTotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public string? PaymentVariableSymbol { get; init; }
    public BalanceStatus BalanceStatus { get; init; }
    public IReadOnlyList<LookupItem> AvailableKingdoms { get; init; } = [];
    public IReadOnlyList<AttendeeViewModel> Attendees { get; init; } = [];
}

public sealed class ContactInput
{
    [Required(ErrorMessage = "Vyplňte jméno hlavního kontaktu.")]
    [StringLength(200)]
    public string PrimaryContactName { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte kontaktní e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail nemá platný tvar.")]
    public string PrimaryEmail { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte telefon.")]
    [StringLength(40)]
    public string PrimaryPhone { get; set; } = "";

    [StringLength(4000)]
    public string? RegistrantNote { get; set; }
}

public sealed class AttendeeInput : IValidatableObject
{
    [Required(ErrorMessage = "Vyplňte jméno.")]
    [StringLength(100)]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte příjmení.")]
    [StringLength(100)]
    public string LastName { get; set; } = "";

    [Range(1900, 2100, ErrorMessage = "Rok narození není platný.")]
    public int BirthYear { get; set; }

    public RegistrationRole Role { get; set; } = RegistrationRole.Player;

    public int? PreferredKingdomId { get; set; }

    public string? ContactEmail { get; set; }

    [StringLength(40)]
    public string? ContactPhone { get; set; }

    [StringLength(200)]
    public string? GuardianName { get; set; }

    [StringLength(100)]
    public string? GuardianRelationship { get; set; }

    public bool GuardianAuthorizationConfirmed { get; set; }

    public bool IsMinor => DateTime.UtcNow.Year - BirthYear < 18;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BirthYear > DateTime.UtcNow.Year + 1)
        {
            yield return new ValidationResult("Rok narození nemůže být v budoucnu.", [nameof(BirthYear)]);
        }

        if (IsMinor)
        {
            if (string.IsNullOrWhiteSpace(GuardianName))
            {
                yield return new ValidationResult("U nezletilého je povinné jméno zákonného zástupce.", [nameof(GuardianName)]);
            }

            if (string.IsNullOrWhiteSpace(GuardianRelationship))
            {
                yield return new ValidationResult("U nezletilého je povinný vztah zákonného zástupce.", [nameof(GuardianRelationship)]);
            }

            if (!GuardianAuthorizationConfirmed)
            {
                yield return new ValidationResult("Potvrďte souhlas zákonného zástupce.", [nameof(GuardianAuthorizationConfirmed)]);
            }
        }

        if (!string.IsNullOrWhiteSpace(ContactEmail) && !new EmailAddressAttribute().IsValid(ContactEmail))
        {
            yield return new ValidationResult("E-mail nemá platný tvar.", [nameof(ContactEmail)]);
        }
    }
}
