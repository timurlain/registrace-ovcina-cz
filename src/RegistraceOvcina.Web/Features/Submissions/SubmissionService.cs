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
                PaidAmount = x.Payments.Sum(p => p.Amount),
                AttendeeCount = x.Registrations.Count(r => r.Status == RegistrationStatus.Active)
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
                pricingService.CalculateBalanceStatus(x.ExpectedTotalAmount, x.PaidAmount),
                x.AttendeeCount))
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
            .Include(x => x.Registrations)
                .ThenInclude(x => x.FoodOrders)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken);

        if (submission is null)
        {
            return null;
        }

        var currentTotal = pricingService.CalculateExpectedTotal(submission.Game, submission.Registrations, submission.VoluntaryDonation);
        var paidAmount = submission.Payments.Sum(x => x.Amount);

        var kingdoms = await db.Kingdoms
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => new LookupItem(x.Id, x.DisplayName))
            .ToListAsync(cancellationToken);

        var mealOptions = await db.MealOptions
            .AsNoTracking()
            .Where(x => x.GameId == submission.GameId && x.IsActive)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var gameDays = new List<DateTime>();
        var current = submission.Game.StartsAtUtc.Date;
        var end = submission.Game.EndsAtUtc.Date;
        while (current <= end)
        {
            gameDays.Add(current);
            current = current.AddDays(1);
        }

        var existingFoodOrders = await db.FoodOrders
            .AsNoTracking()
            .Where(x => x.Registration.SubmissionId == submission.Id)
            .Select(x => new FoodOrderViewModel(x.RegistrationId, x.MealOptionId, x.MealDayUtc))
            .ToListAsync(cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Price breakdown for all non-cancelled submissions (draft + submitted)
        var pricingResult = submission.Status != SubmissionStatus.Cancelled
            ? pricingService.CalculateBreakdown(submission.Game, submission.Registrations, submission.VoluntaryDonation)
            : null;

        return new SubmissionEditorViewModel
        {
            Id = submission.Id,
            GameId = submission.GameId,
            GameName = submission.Game.Name,
            GameStartsAtUtc = submission.Game.StartsAtUtc,
            RegistrationClosesAtUtc = submission.Game.RegistrationClosesAtUtc,
            MealOrderingClosesAtUtc = submission.Game.MealOrderingClosesAtUtc,
            PaymentDueAtUtc = submission.Game.PaymentDueAtUtc,
            CanEditRegistration = submission.Status != SubmissionStatus.Cancelled
                && nowUtc <= submission.Game.RegistrationClosesAtUtc,
            CanEditMeals = submission.Status != SubmissionStatus.Cancelled
                && nowUtc <= submission.Game.MealOrderingClosesAtUtc,
            BankAccount = submission.Game.BankAccount,
            BankAccountName = submission.Game.BankAccountName,
            Status = submission.Status,
            SubmittedAtUtc = submission.SubmittedAtUtc,
            PrimaryContactName = submission.PrimaryContactName,
            GroupName = submission.GroupName,
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
                    x.Person.FirstName,
                    x.Person.LastName,
                    $"{x.Person.FirstName} {x.Person.LastName}",
                    x.Person.BirthYear,
                    x.AttendeeType,
                    x.PlayerSubType,
                    x.AdultRoles,
                    x.PreferredKingdom?.DisplayName,
                    x.PreferredKingdomId,
                    x.CharacterName,
                    x.LodgingPreference,
                    x.RegistrantNote,
                    x.GuardianAuthorizationConfirmed,
                    x.ContactEmail,
                    x.ContactPhone,
                    x.GuardianName,
                    x.GuardianRelationship))
                .ToList(),
            VoluntaryDonation = submission.VoluntaryDonation,
            PriceBreakdown = pricingResult?.Lines ?? [],
            MealDays = gameDays.Select(day => new MealDayViewModel(
                day,
                day.ToString("dddd d. M.", new System.Globalization.CultureInfo("cs-CZ")),
                mealOptions.Select(mo => new MealOptionViewModel(mo.Id, mo.Name, mo.Price)).ToList()
            )).ToList(),
            ExistingFoodOrders = existingFoodOrders,
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
        var submission = await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");
        EnsureEditable(submission);

        submission.PrimaryContactName = input.PrimaryContactName.Trim();
        submission.GroupName = input.GroupName.Trim();
        submission.PrimaryEmail = input.PrimaryEmail.Trim();
        submission.PrimaryPhone = input.PrimaryPhone.Trim();
        submission.RegistrantNote = string.IsNullOrWhiteSpace(input.RegistrantNote) ? null : input.RegistrantNote.Trim();
        submission.VoluntaryDonation = Math.Max(0, input.VoluntaryDonation);
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
                .ThenInclude(x => x.MealOptions)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.Person)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.FoodOrders)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");

        EnsureRegistrationOpen(submission);

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
            AttendeeType = input.AttendeeType,
            PlayerSubType = input.AttendeeType == AttendeeType.Player ? input.PlayerSubType : null,
            AdultRoles = input.AttendeeType == AttendeeType.Adult ? input.ComputedAdultRoles : AdultRoleFlags.None,
            CharacterName = string.IsNullOrWhiteSpace(input.CharacterName) ? null : input.CharacterName.Trim(),
            LodgingPreference = input.LodgingPreference,
            RegistrantNote = string.IsNullOrWhiteSpace(input.AttendeeNote) ? null : input.AttendeeNote.Trim(),
            Status = RegistrationStatus.Active,
            PreferredKingdomId = input.AttendeeType == AttendeeType.Player ? input.PreferredKingdomId : null,
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

        // Save food orders from the attendee form
        SaveFoodOrdersFromInput(db, registration, input, submission.Game);

        RecalculateIfSubmitted(submission);
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
                registration.AttendeeType
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAttendeeAsync(
        int submissionId,
        int registrationId,
        string userId,
        AttendeeInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await db.RegistrationSubmissions
            .Include(x => x.Game)
                .ThenInclude(x => x.MealOptions)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.Person)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.FoodOrders)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");
        EnsureRegistrationOpen(submission);

        var registration = await db.Registrations
            .Include(x => x.Person)
            .Include(x => x.FoodOrders)
            .FirstOrDefaultAsync(x => x.Id == registrationId && x.SubmissionId == submissionId, cancellationToken)
            ?? throw new ValidationException("Účastník nebyl nalezen.");

        if (pricingService.RequiresGuardianData(input.BirthYear))
        {
            if (string.IsNullOrWhiteSpace(input.GuardianName) || string.IsNullOrWhiteSpace(input.GuardianRelationship) || !input.GuardianAuthorizationConfirmed)
            {
                throw new ValidationException("Nezletilý účastník musí mít vyplněného zákonného zástupce a potvrzení souhlasu.");
            }
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Update Person
        registration.Person.FirstName = input.FirstName.Trim();
        registration.Person.LastName = input.LastName.Trim();
        registration.Person.BirthYear = input.BirthYear;
        registration.Person.Email = string.IsNullOrWhiteSpace(input.ContactEmail) ? null : input.ContactEmail.Trim();
        registration.Person.Phone = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim();
        registration.Person.UpdatedAtUtc = nowUtc;

        // Update Registration
        registration.AttendeeType = input.AttendeeType;
        registration.PlayerSubType = input.AttendeeType == AttendeeType.Player ? input.PlayerSubType : null;
        registration.AdultRoles = input.AttendeeType == AttendeeType.Adult ? input.ComputedAdultRoles : AdultRoleFlags.None;
        registration.PreferredKingdomId = input.AttendeeType == AttendeeType.Player ? input.PreferredKingdomId : null;
        registration.CharacterName = string.IsNullOrWhiteSpace(input.CharacterName) ? null : input.CharacterName.Trim();
        registration.LodgingPreference = input.LodgingPreference;
        registration.RegistrantNote = string.IsNullOrWhiteSpace(input.AttendeeNote) ? null : input.AttendeeNote.Trim();
        registration.ContactEmail = string.IsNullOrWhiteSpace(input.ContactEmail) ? null : input.ContactEmail.Trim();
        registration.ContactPhone = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim();
        registration.GuardianName = string.IsNullOrWhiteSpace(input.GuardianName) ? null : input.GuardianName.Trim();
        registration.GuardianRelationship = string.IsNullOrWhiteSpace(input.GuardianRelationship) ? null : input.GuardianRelationship.Trim();
        registration.GuardianAuthorizationConfirmed = input.GuardianAuthorizationConfirmed;
        registration.UpdatedAtUtc = nowUtc;

        // Update food orders from the attendee form
        SaveFoodOrdersFromInput(db, registration, input, submission.Game);

        submission.LastEditedAtUtc = nowUtc;
        RecalculateIfSubmitted(submission);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Registration),
            EntityId = registration.Id.ToString(),
            Action = "AttendeeUpdated",
            ActorUserId = userId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                registration.SubmissionId,
                registration.Person.FirstName,
                registration.Person.LastName,
                registration.AttendeeType
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
        var submission = await db.RegistrationSubmissions
            .Include(x => x.Game)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.Person)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.FoodOrders)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");
        EnsureRegistrationOpen(submission);

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
        RecalculateIfSubmitted(submission);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveFoodOrdersAsync(
        int submissionId,
        string userId,
        List<FoodOrderInput> orders,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await db.RegistrationSubmissions
            .Include(x => x.Registrations).ThenInclude(x => x.FoodOrders)
            .Include(x => x.Game).ThenInclude(x => x.MealOptions)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");

        EnsureMealOrderingOpen(submission);

        var validRegistrationIds = submission.Registrations.Select(x => x.Id).ToHashSet();
        var validMealOptionIds = submission.Game.MealOptions.Where(x => x.IsActive).ToDictionary(x => x.Id);

        // Remove existing food orders for this submission
        foreach (var reg in submission.Registrations)
        {
            db.FoodOrders.RemoveRange(reg.FoodOrders);
        }

        // Add new ones
        foreach (var order in orders)
        {
            if (!validRegistrationIds.Contains(order.RegistrationId)) continue;
            if (!validMealOptionIds.TryGetValue(order.MealOptionId, out var mealOption)) continue;

            db.FoodOrders.Add(new FoodOrder
            {
                RegistrationId = order.RegistrationId,
                MealOptionId = order.MealOptionId,
                MealDayUtc = order.MealDayUtc,
                Price = mealOption.Price
            });
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
                .ThenInclude(x => x.Person)
            .Include(x => x.Registrations)
                .ThenInclude(x => x.FoodOrders)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.RegistrantUserId == userId, cancellationToken)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");

        if (submission.Status != SubmissionStatus.Draft)
        {
            throw new ValidationException("Přihlášku lze odeslat pouze ze stavu rozpracovaná.");
        }

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
        submission.ExpectedTotalAmount = pricingService.CalculateExpectedTotal(submission.Game, submission.Registrations, submission.VoluntaryDonation);
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

    public async Task<PersonSuggestion?> FindExistingPersonAsync(
        string firstName,
        string lastName,
        int gameId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return null;

        firstName = firstName.Trim();
        lastName = lastName.Trim();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var person = await db.People
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                && p.FirstName.ToLower() == firstName.ToLower()
                && p.LastName.ToLower() == lastName.ToLower())
            .OrderByDescending(p => p.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (person is null)
            return null;

        // Find the most recent registration to get attendee type info
        var lastRegistration = await db.Registrations
            .AsNoTracking()
            .Where(r => r.PersonId == person.Id && r.Status == RegistrationStatus.Active)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // Find the most recent character appearance to get last kingdom
        var lastAppearance = await db.CharacterAppearances
            .AsNoTracking()
            .Include(ca => ca.AssignedKingdom)
            .Where(ca => ca.Character.PersonId == person.Id && ca.AssignedKingdomId != null)
            .OrderByDescending(ca => ca.GameId)
            .FirstOrDefaultAsync(cancellationToken);

        return new PersonSuggestion(
            PersonId: person.Id,
            FullName: $"{person.FirstName} {person.LastName}",
            BirthYear: person.BirthYear,
            Email: person.Email,
            Phone: person.Phone,
            GuardianName: lastRegistration?.GuardianName,
            GuardianRelationship: lastRegistration?.GuardianRelationship,
            LastKingdomId: lastAppearance?.AssignedKingdomId,
            LastKingdomName: lastAppearance?.AssignedKingdom?.DisplayName,
            LastAttendeeType: lastRegistration?.AttendeeType ?? AttendeeType.Player,
            LastPlayerSubType: lastRegistration?.PlayerSubType,
            LastAdultRoles: lastRegistration?.AdultRoles ?? AdultRoleFlags.None);
    }

    public async Task DeleteSubmissionAsync(
        int submissionId,
        string userId,
        bool isStaff,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var submission = await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId, ct)
            ?? throw new ValidationException("Přihláška nebyla nalezena.");

        if (!isStaff && submission.RegistrantUserId != userId)
        {
            throw new ValidationException("Nemáte oprávnění smazat tuto přihlášku.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        submission.IsDeleted = true;
        submission.LastEditedAtUtc = nowUtc;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(RegistrationSubmission),
            EntityId = submission.Id.ToString(),
            Action = "SubmissionDeleted",
            ActorUserId = userId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new { submission.GameId, IsStaff = isStaff })
        });

        await db.SaveChangesAsync(ct);
    }

    private static void EnsureEditable(RegistrationSubmission submission)
    {
        if (submission.Status == SubmissionStatus.Cancelled)
            throw new ValidationException("Zrušená přihláška nelze upravovat.");
    }

    private void EnsureRegistrationOpen(RegistrationSubmission submission)
    {
        EnsureEditable(submission);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now > submission.Game.RegistrationClosesAtUtc)
            throw new ValidationException("Registrace pro tuto hru je už uzavřená.");
    }

    private void EnsureMealOrderingOpen(RegistrationSubmission submission)
    {
        EnsureEditable(submission);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now > submission.Game.MealOrderingClosesAtUtc)
            throw new ValidationException("Objednávání jídel pro tuto hru je už uzavřené.");
    }

    private void RecalculateIfSubmitted(RegistrationSubmission submission)
    {
        if (submission.Status == SubmissionStatus.Submitted)
        {
            submission.ExpectedTotalAmount = pricingService.CalculateExpectedTotal(submission.Game, submission.Registrations, submission.VoluntaryDonation);
        }
    }

    private static void SaveFoodOrdersFromInput(
        ApplicationDbContext db,
        Registration registration,
        AttendeeInput input,
        Game game)
    {
        if (input.MealSelections.Count == 0)
            return;

        var validMealOptions = game.MealOptions?
            .Where(x => x.IsActive)
            .ToDictionary(x => x.Id)
            ?? [];

        // Remove existing food orders for this registration
        if (registration.FoodOrders is { Count: > 0 })
        {
            db.FoodOrders.RemoveRange(registration.FoodOrders);
            registration.FoodOrders.Clear();
        }

        // Add new ones from form input
        foreach (var (dayTicks, mealOptionId) in input.MealSelections)
        {
            if (mealOptionId is null || !validMealOptions.TryGetValue(mealOptionId.Value, out var mealOption))
                continue;

            var foodOrder = new FoodOrder
            {
                RegistrationId = registration.Id,
                MealOptionId = mealOption.Id,
                MealDayUtc = new DateTime(dayTicks, DateTimeKind.Utc),
                Price = mealOption.Price
            };
            db.FoodOrders.Add(foodOrder);
            registration.FoodOrders.Add(foodOrder);
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

    public async Task<int> RepeatSubmissionAsync(
        int sourceSubmissionId,
        int targetGameId,
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var game = await db.Games.FirstOrDefaultAsync(x => x.Id == targetGameId, cancellationToken)
            ?? throw new ValidationException("Vybraná hra neexistuje.");

        if (!game.IsPublished)
            throw new ValidationException("Tuto hru zatím nelze přihlašovat.");

        if (game.RegistrationClosesAtUtc < nowUtc)
            throw new ValidationException("Registrace pro tuto hru už byla uzavřena.");

        var existingDraft = await db.RegistrationSubmissions
            .Where(x => x.GameId == targetGameId && x.RegistrantUserId == user.Id && !x.IsDeleted)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingDraft.HasValue)
            throw new ValidationException("Pro tuto hru už máte přihlášku. Otevřete ji a upravte.");

        var source = await db.RegistrationSubmissions
            .AsNoTracking()
            .Include(x => x.Registrations.Where(r => r.Status == RegistrationStatus.Active))
            .ThenInclude(r => r.Person)
            .FirstOrDefaultAsync(x => x.Id == sourceSubmissionId && x.RegistrantUserId == user.Id, cancellationToken)
            ?? throw new ValidationException("Zdrojová přihláška nebyla nalezena.");

        var submission = new RegistrationSubmission
        {
            GameId = targetGameId,
            RegistrantUserId = user.Id,
            PrimaryContactName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? user.UserName ?? "" : user.DisplayName,
            GroupName = source.GroupName,
            PrimaryEmail = user.Email ?? "",
            PrimaryPhone = user.PhoneNumber ?? "",
            Status = SubmissionStatus.Draft,
            LastEditedAtUtc = nowUtc,
            ExpectedTotalAmount = 0m
        };

        db.RegistrationSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var reg in source.Registrations)
        {
            var cloned = new Registration
            {
                SubmissionId = submission.Id,
                PersonId = reg.PersonId,
                AttendeeType = reg.AttendeeType,
                PlayerSubType = reg.PlayerSubType,
                AdultRoles = reg.AdultRoles,
                CharacterName = reg.CharacterName,
                LodgingPreference = reg.LodgingPreference,
                PreferredKingdomId = reg.PreferredKingdomId,
                ContactEmail = reg.ContactEmail,
                ContactPhone = reg.ContactPhone,
                GuardianName = reg.GuardianName,
                GuardianRelationship = reg.GuardianRelationship,
                GuardianAuthorizationConfirmed = reg.GuardianAuthorizationConfirmed,
                RegistrantNote = reg.RegistrantNote,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            db.Registrations.Add(cloned);
        }

        await db.SaveChangesAsync(cancellationToken);

        var clonedRegistrations = await db.Registrations
            .Include(r => r.Person)
            .Where(r => r.SubmissionId == submission.Id)
            .ToListAsync(cancellationToken);
        submission.ExpectedTotalAmount = pricingService.CalculateExpectedTotal(game, clonedRegistrations);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(RegistrationSubmission),
            EntityId = submission.Id.ToString(),
            Action = "SubmissionRepeated",
            ActorUserId = user.Id,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new { SourceSubmissionId = sourceSubmissionId, submission.GameId })
        });

        await db.SaveChangesAsync(cancellationToken);
        return submission.Id;
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
    BalanceStatus BalanceStatus,
    int AttendeeCount);

public sealed record AttendeeViewModel(
    int RegistrationId,
    string FirstName,
    string LastName,
    string FullName,
    int BirthYear,
    AttendeeType AttendeeType,
    PlayerSubType? PlayerSubType,
    AdultRoleFlags AdultRoles,
    string? PreferredKingdom,
    int? PreferredKingdomId,
    string? CharacterName,
    LodgingPreference? LodgingPreference,
    string? AttendeeNote,
    bool GuardianAuthorizationConfirmed,
    string? ContactEmail,
    string? ContactPhone,
    string? GuardianName,
    string? GuardianRelationship);

public sealed class SubmissionEditorViewModel
{
    public int Id { get; init; }
    public int GameId { get; init; }
    public string GameName { get; init; } = "";
    public DateTime GameStartsAtUtc { get; init; }
    public DateTime RegistrationClosesAtUtc { get; init; }
    public DateTime MealOrderingClosesAtUtc { get; init; }
    public DateTime PaymentDueAtUtc { get; init; }
    public bool CanEditRegistration { get; init; }
    public bool CanEditMeals { get; init; }
    public string BankAccount { get; init; } = "";
    public string BankAccountName { get; init; } = "";
    public SubmissionStatus Status { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public string PrimaryContactName { get; init; } = "";
    public string GroupName { get; init; } = "";
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
    public IReadOnlyList<MealDayViewModel> MealDays { get; init; } = [];
    public IReadOnlyList<FoodOrderViewModel> ExistingFoodOrders { get; init; } = [];
    public decimal VoluntaryDonation { get; init; }
    public IReadOnlyList<PriceBreakdownLine> PriceBreakdown { get; init; } = [];
}

public sealed record PriceBreakdownLine(string Label, int Count, decimal UnitPrice, decimal Total);

public sealed class ContactInput
{
    [Required(ErrorMessage = "Vyplňte jméno hlavního kontaktu.")]
    [StringLength(200)]
    public string PrimaryContactName { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte název skupiny nebo rodiny.")]
    [StringLength(200)]
    public string GroupName { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte kontaktní e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail nemá platný tvar.")]
    public string PrimaryEmail { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte telefon.")]
    [StringLength(40)]
    public string PrimaryPhone { get; set; } = "";

    [StringLength(4000)]
    public string? RegistrantNote { get; set; }

    [Range(0, 100000, ErrorMessage = "Příspěvek musí být kladný nebo nulový.")]
    public decimal VoluntaryDonation { get; set; }
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

    public AttendeeType AttendeeType { get; set; } = AttendeeType.Player;
    public PlayerSubType? PlayerSubType { get; set; }
    public AdultRoleFlags AdultRoles { get; set; }

    [StringLength(200)]
    public string? CharacterName { get; set; }

    public LodgingPreference? LodgingPreference { get; set; }

    [StringLength(4000)]
    public string? AttendeeNote { get; set; }

    public int? PreferredKingdomId { get; set; }

    public string? ContactEmail { get; set; }

    [StringLength(40)]
    public string? ContactPhone { get; set; }

    [StringLength(200)]
    public string? GuardianName { get; set; }

    [StringLength(100)]
    public string? GuardianRelationship { get; set; }

    public bool GuardianAuthorizationConfirmed { get; set; }

    /// <summary>
    /// Per-day meal selections: key = day ticks (UTC), value = MealOption ID.
    /// Empty or missing entry means no meal for that day.
    /// </summary>
    public Dictionary<long, int?> MealSelections { get; set; } = [];

    // Individual bool properties for multi-checkbox AdultRoles binding
    public bool AdultRole_PlayMonster { get; set; }
    public bool AdultRole_OrganizationHelper { get; set; }
    public bool AdultRole_TechSupport { get; set; }
    public bool AdultRole_RangerLeader { get; set; }
    public bool AdultRole_Spectator { get; set; }

    // Computed from individual bools
    public AdultRoleFlags ComputedAdultRoles =>
        (AdultRole_PlayMonster ? AdultRoleFlags.PlayMonster : 0) |
        (AdultRole_OrganizationHelper ? AdultRoleFlags.OrganizationHelper : 0) |
        (AdultRole_TechSupport ? AdultRoleFlags.TechSupport : 0) |
        (AdultRole_RangerLeader ? AdultRoleFlags.RangerLeader : 0) |
        (AdultRole_Spectator ? AdultRoleFlags.Spectator : 0);

    public bool IsMinor => DateTime.UtcNow.Year - BirthYear < 18;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BirthYear > DateTime.UtcNow.Year + 1)
        {
            yield return new ValidationResult("Rok narození nemůže být v budoucnu.", [nameof(BirthYear)]);
        }

        if (AttendeeType == AttendeeType.Player && PlayerSubType is null)
        {
            yield return new ValidationResult("Vyberte kategorii hráče.", [nameof(PlayerSubType)]);
        }

        if (AttendeeType == AttendeeType.Adult && ComputedAdultRoles == AdultRoleFlags.None)
        {
            yield return new ValidationResult("Vyberte alespoň jednu roli dospělého.", [nameof(AdultRoles)]);
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

public sealed record MealDayViewModel(
    DateTime DayUtc,
    string DayLabel,
    IReadOnlyList<MealOptionViewModel> Options);

public sealed record MealOptionViewModel(int Id, string Name, decimal Price);

public sealed record FoodOrderViewModel(int RegistrationId, int MealOptionId, DateTime MealDayUtc);

public sealed record FoodOrderInput(int RegistrationId, int MealOptionId, DateTime MealDayUtc);

public sealed record PersonSuggestion(
    int PersonId,
    string FullName,
    int BirthYear,
    string? Email,
    string? Phone,
    string? GuardianName,
    string? GuardianRelationship,
    int? LastKingdomId,
    string? LastKingdomName,
    AttendeeType LastAttendeeType,
    PlayerSubType? LastPlayerSubType,
    AdultRoleFlags LastAdultRoles);
