using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Infrastructure;

namespace RegistraceOvcina.Web.Features.HistoricalImport;

public sealed class HistoricalImportService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    private const string GoogleFormFormat = "google-form";
    private const string LegacyWorkbookFormat = "legacy-workbook";

    private static readonly string[] GenericCharacterNames =
    [
        "prisera",
        "prisery",
        "lide",
        "elfove",
        "trpaslici",
        "novy arnor"
    ];

    public async Task<HistoricalImportPageModel> GetPageAsync(
        int? highlightedBatchId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var batches = await db.HistoricalImportBatches
            .AsNoTracking()
            .Include(x => x.Game)
            .OrderByDescending(x => x.ImportedAtUtc)
            .Take(12)
            .Select(x => new HistoricalImportBatchListItem(
                x.Id,
                x.Label,
                x.SourceFormat,
                x.SourceFileName,
                x.Game.Name,
                x.ImportedAtUtc,
                x.TotalSourceRows,
                x.HouseholdCount,
                x.RegistrationCount,
                x.PersonCreatedCount,
                x.PersonMatchedCount,
                x.CharacterCreatedCount,
                x.WarningCount))
            .ToListAsync(cancellationToken);

        var selectedBatchId = highlightedBatchId ?? batches.FirstOrDefault()?.Id;
        HistoricalImportBatchDetails? highlightedBatch = null;

        if (selectedBatchId.HasValue)
        {
            var batch = await db.HistoricalImportBatches
                .AsNoTracking()
                .Include(x => x.Game)
                .SingleOrDefaultAsync(x => x.Id == selectedBatchId.Value, cancellationToken);

            if (batch is not null)
            {
                var warnings = await db.HistoricalImportRows
                    .AsNoTracking()
                    .Where(x => x.LastBatchId == batch.Id && x.WarningMessage != null)
                    .OrderBy(x => x.SourceSheet)
                    .ThenBy(x => x.SourceLabel)
                    .Select(x => new HistoricalImportWarningItem(
                        x.SourceSheet,
                        x.SourceLabel,
                        x.WarningMessage!))
                    .ToListAsync(cancellationToken);

                highlightedBatch = new HistoricalImportBatchDetails(
                    batch.Id,
                    batch.Label,
                    batch.SourceFormat,
                    batch.SourceFileName,
                    batch.Game.Name,
                    batch.ImportedAtUtc,
                    batch.TotalSourceRows,
                    batch.HouseholdCount,
                    batch.RegistrationCount,
                    batch.PersonCreatedCount,
                    batch.PersonMatchedCount,
                    batch.CharacterCreatedCount,
                    batch.WarningCount,
                    warnings);
            }
        }

        return new HistoricalImportPageModel(batches, highlightedBatch);
    }

    public async Task<HistoricalImportResult> ImportWorkbookAsync(
        HistoricalImportCommand command,
        Stream workbookStream,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (command.EndsAtLocal <= command.StartsAtLocal)
        {
            throw new ValidationException("Konec historické hry musí být po začátku.");
        }

        if (string.IsNullOrWhiteSpace(command.SourceFileName))
        {
            throw new ValidationException("Vyberte Excel soubor pro import.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        using var workbook = new XLWorkbook(workbookStream);
        var parsedRows = ParseWorkbook(workbook);

        if (parsedRows.Count == 0)
        {
            throw new ValidationException("V importovaném souboru nebyly nalezeny žádné použitelné řádky.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await GetOrCreateHistoricalGameAsync(db, command, nowUtc, cancellationToken);
        var batch = new HistoricalImportBatch
        {
            Label = command.Label,
            SourceFormat = parsedRows[0].SourceFormat,
            SourceFileName = command.SourceFileName.Trim(),
            GameId = game.Id,
            ImportedByUserId = actorUserId,
            ImportedAtUtc = nowUtc,
            TotalSourceRows = parsedRows.Count
        };

        db.HistoricalImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        var sourceRowMap = await LoadExistingSourceRowsAsync(db, parsedRows, cancellationToken);
        var kingdomCache = await db.Kingdoms
            .ToDictionaryAsync(x => NormalizeComparisonText(x.DisplayName), x => x, cancellationToken);
        var submissionCache = new Dictionary<string, RegistrationSubmission>(StringComparer.Ordinal);
        var personCache = new Dictionary<string, Person>(StringComparer.Ordinal);
        var registrationCache = new Dictionary<string, Registration>(StringComparer.Ordinal);
        var characterCache = new Dictionary<string, Character>(StringComparer.Ordinal);
        var touchedSubmissionIds = new HashSet<int>();
        var touchedRegistrationIds = new HashSet<int>();
        var personCreatedCount = 0;
        var personMatchedCount = 0;
        var characterCreatedCount = 0;
        var warningCount = 0;

        foreach (var parsedRow in parsedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var warningParts = new List<string>();
            if (parsedRow.Warning is { Length: > 0 })
            {
                warningParts.Add(parsedRow.Warning);
            }

            var sourceKey = BuildSourceLookupKey(parsedRow.SourceSheet, parsedRow.SourceKey);
            sourceRowMap.TryGetValue(sourceKey, out var sourceRow);

            var submission = await ResolveSubmissionAsync(
                db,
                sourceRow,
                parsedRow,
                submissionCache,
                game,
                nowUtc,
                cancellationToken);

            touchedSubmissionIds.Add(submission.Id);

            var personResolution = await ResolvePersonAsync(
                db,
                sourceRow,
                parsedRow,
                personCache,
                game.StartsAtUtc.Year,
                nowUtc,
                cancellationToken);

            var person = personResolution.Person;
            if (personResolution.WasCreated)
            {
                personCreatedCount++;
            }
            else
            {
                personMatchedCount++;
            }

            if (personResolution.Warning is { Length: > 0 })
            {
                warningParts.Add(personResolution.Warning);
            }

            var registration = await ResolveRegistrationAsync(
                db,
                sourceRow,
                parsedRow,
                registrationCache,
                submission,
                person,
                kingdomCache,
                nowUtc,
                cancellationToken);

            touchedRegistrationIds.Add(registration.Id);

            Character? character = null;
            if (!string.IsNullOrWhiteSpace(parsedRow.CharacterName))
            {
                var characterResolution = await ResolveCharacterAsync(
                    db,
                sourceRow,
                parsedRow,
                characterCache,
                game,
                registration,
                person,
                kingdomCache,
                nowUtc,
                cancellationToken);

                character = characterResolution.Character;
                if (characterResolution.WasCreated)
                {
                    characterCreatedCount++;
                }

                if (characterResolution.Warning is { Length: > 0 })
                {
                    warningParts.Add(characterResolution.Warning);
                }
            }

            var warningMessage = warningParts.Count == 0
                ? null
                : string.Join(" ", warningParts.Distinct(StringComparer.Ordinal));

            if (warningMessage is not null)
            {
                warningCount++;
            }

            UpsertSourceRow(
                db,
                sourceRowMap,
                batch.Id,
                parsedRow,
                sourceRow,
                person.Id,
                submission.Id,
                registration.Id,
                character?.Id,
                warningMessage,
                nowUtc);
        }

        batch.HouseholdCount = touchedSubmissionIds.Count;
        batch.RegistrationCount = touchedRegistrationIds.Count;
        batch.PersonCreatedCount = personCreatedCount;
        batch.PersonMatchedCount = personMatchedCount;
        batch.CharacterCreatedCount = characterCreatedCount;
        batch.WarningCount = warningCount;
        batch.NotesJson = JsonSerializer.Serialize(new
        {
            GameId = game.Id,
            GameName = game.Name
        });

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(HistoricalImportBatch),
            EntityId = batch.Id.ToString(CultureInfo.InvariantCulture),
            Action = "HistoricalImportCompleted",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                batch.Label,
                batch.SourceFormat,
                batch.SourceFileName,
                batch.GameId,
                batch.TotalSourceRows,
                batch.HouseholdCount,
                batch.RegistrationCount,
                batch.PersonCreatedCount,
                batch.PersonMatchedCount,
                batch.CharacterCreatedCount,
                batch.WarningCount
            })
        });

        await db.SaveChangesAsync(cancellationToken);

        return new HistoricalImportResult(batch.Id);
    }

    private static IReadOnlyList<ParsedHistoricalRow> ParseWorkbook(XLWorkbook workbook)
    {
        if (workbook.TryGetWorksheet("Registrační formulář", out var googleFormWorksheet))
        {
            return ParseGoogleFormWorksheet(googleFormWorksheet);
        }

        var rows = new List<ParsedHistoricalRow>();

        if (workbook.TryGetWorksheet("Deti", out var detiWorksheet))
        {
            rows.AddRange(ParseLegacyDetiWorksheet(detiWorksheet));
        }

        if (workbook.TryGetWorksheet("Orgove", out var orgoveWorksheet))
        {
            rows.AddRange(ParseLegacyOrgoveWorksheet(orgoveWorksheet));
        }

        if (rows.Count > 0)
        {
            return rows;
        }

        throw new ValidationException(
            "Soubor neobsahuje podporovaný historický formát. Podporován je list 'Registrační formulář' nebo legacy listy 'Deti' a 'Orgove'.");
    }

    private static IReadOnlyList<ParsedHistoricalRow> ParseGoogleFormWorksheet(IXLWorksheet worksheet)
    {
        var rows = new List<ParsedHistoricalRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var fullName = GetCellText(row, 3);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var typeInfo = ParseParticipantType(
                GetCellText(row, 6),
                isPlayerFlag: IsTruthy(GetCellText(row, 15)),
                isNpcFlag: IsTruthy(GetCellText(row, 16)),
                isMonsterFlag: IsTruthy(GetCellText(row, 17)),
                isTechFlag: IsTruthy(GetCellText(row, 18)),
                forceAdult: false);

            rows.Add(new ParsedHistoricalRow(
                GoogleFormFormat,
                worksheet.Name,
                BuildGoogleFormSourceKey(
                    GetCellText(row, 1),
                    GetCellText(row, 2),
                    fullName,
                    GetCellText(row, 13)),
                BuildSourceLabel(GetCellText(row, 2), fullName),
                GetCellText(row, 2),
                fullName,
                NormalizeCharacterName(GetCellText(row, 4), GetCellText(row, 19)),
                GetCellText(row, 11),
                GetCellText(row, 12),
                GetCellText(row, 13),
                GetCellText(row, 14),
                GetCellText(row, 19),
                ParseSubmittedAtUtc(GetCellText(row, 1)),
                GetCellText(row, 5),
                typeInfo.AttendeeType,
                typeInfo.PlayerSubType,
                typeInfo.AdultRoles,
                null));
        }

        return rows;
    }

    private static IReadOnlyList<ParsedHistoricalRow> ParseLegacyDetiWorksheet(IXLWorksheet worksheet)
    {
        var rows = new List<ParsedHistoricalRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var fullName = GetCellText(row, 2);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var typeInfo = ParseParticipantType(
                GetCellText(row, 4),
                isPlayerFlag: false,
                isNpcFlag: false,
                isMonsterFlag: false,
                isTechFlag: false,
                forceAdult: false);

            rows.Add(new ParsedHistoricalRow(
                LegacyWorkbookFormat,
                worksheet.Name,
                BuildLegacySourceKey(
                    worksheet.Name,
                    GetCellText(row, 1),
                    fullName,
                    GetCellText(row, 7),
                    GetCellText(row, 8)),
                BuildSourceLabel(GetCellText(row, 1), fullName),
                GetCellText(row, 1),
                fullName,
                null,
                GetCellText(row, 5),
                GetCellText(row, 6),
                GetCellText(row, 7),
                GetCellText(row, 8),
                null,
                null,
                GetCellText(row, 3),
                typeInfo.AttendeeType,
                typeInfo.PlayerSubType,
                typeInfo.AdultRoles,
                null));
        }

        return rows;
    }

    private static IReadOnlyList<ParsedHistoricalRow> ParseLegacyOrgoveWorksheet(IXLWorksheet worksheet)
    {
        var rows = new List<ParsedHistoricalRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var fullName = GetCellText(row, 1);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var roleText = GetCellText(row, 3);
            var roleInfo = ParseLegacyOrganizerRole(roleText);

            rows.Add(new ParsedHistoricalRow(
                LegacyWorkbookFormat,
                worksheet.Name,
                BuildLegacySourceKey(
                    worksheet.Name,
                    string.Empty,
                    fullName,
                    GetCellText(row, 5),
                    GetCellText(row, 6)),
                BuildSourceLabel(roleInfo.PreferredKingdomName, fullName),
                roleInfo.PreferredKingdomName,
                fullName,
                null,
                null,
                AppendUniqueText(GetCellText(row, 4), roleText),
                GetCellText(row, 5),
                GetCellText(row, 6),
                roleInfo.PreferredKingdomName,
                null,
                GetCellText(row, 2),
                AttendeeType.Adult,
                null,
                roleInfo.AdultRoles,
                null));
        }

        return rows;
    }

    private static ParticipantTypeInfo ParseParticipantType(
        string? rawType,
        bool isPlayerFlag,
        bool isNpcFlag,
        bool isMonsterFlag,
        bool isTechFlag,
        bool forceAdult)
    {
        var normalized = NormalizeComparisonText(rawType);
        var attendeeType = forceAdult || (!isPlayerFlag && !normalized.Contains("hrajici"))
            ? AttendeeType.Adult
            : AttendeeType.Player;

        PlayerSubType? playerSubType = null;
        var adultRoles = AdultRoleFlags.None;

        if (attendeeType == AttendeeType.Player)
        {
            if (normalized.Contains("pvp") || normalized.Contains("10+"))
            {
                playerSubType = PlayerSubType.Pvp;
            }
            else if (normalized.Contains("8+"))
            {
                playerSubType = PlayerSubType.Independent;
            }
            else if (normalized.Contains("hranic") || normalized.Contains("skupince"))
            {
                playerSubType = PlayerSubType.WithRanger;
            }
            else if (normalized.Contains("doprovodu rodic"))
            {
                playerSubType = PlayerSubType.WithParent;
            }
            else
            {
                playerSubType = PlayerSubType.Independent;
            }
        }
        else
        {
            if (isNpcFlag || normalized.Contains("organizaci") || normalized.Contains("obchodnik") || normalized.Contains("priruci") || normalized.Contains("fotograf") || normalized.Contains("vladce"))
            {
                adultRoles |= AdultRoleFlags.OrganizationHelper;
            }

            if (isMonsterFlag || normalized.Contains("prisera") || normalized.Contains("skret") || normalized.Contains("kostlivec") || normalized.Contains("vsedma") || normalized.Contains("vedma"))
            {
                adultRoles |= AdultRoleFlags.PlayMonster;
            }

            if (isTechFlag || normalized.Contains("technickou organizac"))
            {
                adultRoles |= AdultRoleFlags.TechSupport;
            }

            if (normalized.Contains("hranic"))
            {
                adultRoles |= AdultRoleFlags.RangerLeader;
            }

            if (normalized.Contains("prihlizej"))
            {
                adultRoles |= AdultRoleFlags.Spectator;
            }
        }

        return new ParticipantTypeInfo(attendeeType, playerSubType, adultRoles);
    }

    private static LegacyOrganizerRoleInfo ParseLegacyOrganizerRole(string? rawRole)
    {
        var roleText = rawRole?.Trim() ?? string.Empty;
        var normalized = NormalizeComparisonText(roleText);
        var preferredKingdom = default(string);
        var adultRoles = AdultRoleFlags.None;

        if (roleText.Contains(" - ", StringComparison.Ordinal))
        {
            var parts = roleText.Split(" - ", 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                preferredKingdom = parts[0];
                roleText = parts[1];
                normalized = NormalizeComparisonText(roleText);
            }
        }

        if (normalized.Contains("hranic"))
        {
            adultRoles |= AdultRoleFlags.RangerLeader;
        }

        if (normalized.Contains("fotograf") || normalized.Contains("obchodnik") || normalized.Contains("priruci") || normalized.Contains("vladce"))
        {
            adultRoles |= AdultRoleFlags.OrganizationHelper;
        }

        if (normalized.Contains("prisera") || normalized.Contains("skret") || normalized.Contains("kostlivec"))
        {
            adultRoles |= AdultRoleFlags.PlayMonster;
        }

        if (normalized.Contains("prihlizej"))
        {
            adultRoles |= AdultRoleFlags.Spectator;
        }

        if (adultRoles == AdultRoleFlags.None)
        {
            adultRoles = AdultRoleFlags.OrganizationHelper;
        }

        return new LegacyOrganizerRoleInfo(adultRoles, preferredKingdom);
    }

    private static DateTime? ParseSubmittedAtUtc(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (DateTime.TryParseExact(
                rawValue.Trim(),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return CzechTime.ToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified));
        }

        return null;
    }

    private async Task<Game> GetOrCreateHistoricalGameAsync(
        ApplicationDbContext db,
        HistoricalImportCommand command,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var startsAtUtc = CzechTime.ToUtc(command.StartsAtLocal);
        var endsAtUtc = CzechTime.ToUtc(command.EndsAtLocal);

        var game = await db.Games
            .FirstOrDefaultAsync(
                x => x.Name == command.GameName
                     && x.StartsAtUtc == startsAtUtc
                     && x.EndsAtUtc == endsAtUtc,
                cancellationToken);

        if (game is not null)
        {
            return game;
        }

        game = new Game
        {
            Name = command.GameName,
            Description = $"Historický import: {command.Label}",
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            RegistrationClosesAtUtc = startsAtUtc,
            MealOrderingClosesAtUtc = startsAtUtc,
            PaymentDueAtUtc = startsAtUtc,
            AssignmentFreezeAtUtc = null,
            PlayerBasePrice = 0m,
            AdultHelperBasePrice = 0m,
            BankAccount = "HISTORICAL-IMPORT",
            BankAccountName = "Historický import",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            TargetPlayerCountTotal = 0,
            IsPublished = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.Games.Add(game);
        await db.SaveChangesAsync(cancellationToken);

        return game;
    }

    private static async Task<Dictionary<string, HistoricalImportRow>> LoadExistingSourceRowsAsync(
        ApplicationDbContext db,
        IReadOnlyList<ParsedHistoricalRow> parsedRows,
        CancellationToken cancellationToken)
    {
        var groupedKeys = parsedRows
            .GroupBy(x => x.SourceSheet, x => x.SourceKey, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var sourceFormat = parsedRows[0].SourceFormat;
        var existingRows = await db.HistoricalImportRows
            .Where(x => x.SourceFormat == sourceFormat)
            .ToListAsync(cancellationToken);

        return existingRows
            .Where(x => groupedKeys.TryGetValue(x.SourceSheet, out var keys) && keys.Contains(x.SourceKey))
            .ToDictionary(x => BuildSourceLookupKey(x.SourceSheet, x.SourceKey), StringComparer.Ordinal);
    }

    private async Task<RegistrationSubmission> ResolveSubmissionAsync(
        ApplicationDbContext db,
        HistoricalImportRow? sourceRow,
        ParsedHistoricalRow parsedRow,
        Dictionary<string, RegistrationSubmission> submissionCache,
        Game game,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (sourceRow?.LinkedSubmissionId is int linkedSubmissionId)
        {
            var linkedSubmission = await db.RegistrationSubmissions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == linkedSubmissionId, cancellationToken);

            if (linkedSubmission is not null)
            {
                return linkedSubmission;
            }
        }

        var householdKey = BuildHouseholdKey(parsedRow);
        if (submissionCache.TryGetValue(householdKey, out var cachedSubmission))
        {
            HydrateSubmissionContact(cachedSubmission, parsedRow);
            return cachedSubmission;
        }

        var surrogateEmail = BuildSurrogateEmail(game.Id, householdKey);
        var user = await db.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == surrogateEmail.ToUpperInvariant(), cancellationToken);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = surrogateEmail,
                NormalizedUserName = surrogateEmail.ToUpperInvariant(),
                Email = surrogateEmail,
                NormalizedEmail = surrogateEmail.ToUpperInvariant(),
                EmailConfirmed = false,
                DisplayName = BuildHistoricalDisplayName(parsedRow),
                IsActive = false,
                CreatedAtUtc = nowUtc,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        var submission = await db.RegistrationSubmissions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.GameId == game.Id && x.RegistrantUserId == user.Id,
                cancellationToken);

        if (submission is null)
        {
            submission = new RegistrationSubmission
            {
                GameId = game.Id,
                RegistrantUserId = user.Id,
                PrimaryContactName = BuildPrimaryContactName(parsedRow),
                PrimaryEmail = string.IsNullOrWhiteSpace(parsedRow.Email) ? surrogateEmail : parsedRow.Email.Trim(),
                PrimaryPhone = string.IsNullOrWhiteSpace(parsedRow.Phone) ? "neuvedeno" : parsedRow.Phone.Trim(),
                Status = SubmissionStatus.Submitted,
                SubmittedAtUtc = parsedRow.SubmittedAtUtc ?? game.StartsAtUtc,
                LastEditedAtUtc = nowUtc,
                ExpectedTotalAmount = 0m,
                RegistrantNote = parsedRow.Note?.Trim(),
                IsDeleted = false
            };

            db.RegistrationSubmissions.Add(submission);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            HydrateSubmissionContact(submission, parsedRow);
        }

        submissionCache[householdKey] = submission;
        return submission;
    }

    private async Task<PersonResolution> ResolvePersonAsync(
        ApplicationDbContext db,
        HistoricalImportRow? sourceRow,
        ParsedHistoricalRow parsedRow,
        Dictionary<string, Person> personCache,
        int eventYear,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (sourceRow?.LinkedPersonId is int linkedPersonId)
        {
            var linkedPerson = await db.People.FirstOrDefaultAsync(x => x.Id == linkedPersonId, cancellationToken);
            if (linkedPerson is not null)
            {
                return new PersonResolution(linkedPerson, false, sourceRow.WarningMessage);
            }
        }

        var warningParts = new List<string>();
        var participantKind = parsedRow.AttendeeType;
        var birthYearResolution = ResolveBirthYear(parsedRow.RawAge, participantKind, eventYear);
        if (birthYearResolution.Warning is { Length: > 0 })
        {
            warningParts.Add(birthYearResolution.Warning);
        }

        var nameResolution = SplitFullName(parsedRow.FullName);
        if (nameResolution.Warning is { Length: > 0 })
        {
            warningParts.Add(nameResolution.Warning);
        }

        var cacheKey = BuildPersonCacheKey(
            nameResolution.FirstName,
            nameResolution.LastName,
            birthYearResolution.BirthYear,
            parsedRow.Email,
            parsedRow.Phone);

        if (personCache.TryGetValue(cacheKey, out var cachedPerson))
        {
            return new PersonResolution(
                cachedPerson,
                false,
                warningParts.Count == 0 ? null : string.Join(" ", warningParts));
        }

        var normalizedEmail = NormalizeEmail(parsedRow.Email);
        var normalizedPhone = NormalizePhone(parsedRow.Phone);

        var candidates = await db.People
            .Where(x =>
                x.FirstName == nameResolution.FirstName
                && x.LastName == nameResolution.LastName
                && x.BirthYear == birthYearResolution.BirthYear)
            .ToListAsync(cancellationToken);

        Person? matchedPerson = null;
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            matchedPerson = candidates.SingleOrDefault(x => NormalizeEmail(x.Email) == normalizedEmail);
        }

        if (matchedPerson is null && !string.IsNullOrWhiteSpace(normalizedPhone))
        {
            matchedPerson = candidates.SingleOrDefault(x => NormalizePhone(x.Phone) == normalizedPhone);
        }

        if (matchedPerson is null && string.IsNullOrWhiteSpace(normalizedEmail) && string.IsNullOrWhiteSpace(normalizedPhone) && candidates.Count == 1)
        {
            matchedPerson = candidates[0];
        }

        if (matchedPerson is not null)
        {
            HydratePersonContact(matchedPerson, parsedRow);
            personCache[cacheKey] = matchedPerson;
            return new PersonResolution(
                matchedPerson,
                false,
                warningParts.Count == 0 ? null : string.Join(" ", warningParts));
        }

        var createdPerson = new Person
        {
            FirstName = nameResolution.FirstName,
            LastName = nameResolution.LastName,
            BirthYear = birthYearResolution.BirthYear,
            Email = parsedRow.Email?.Trim(),
            Phone = parsedRow.Phone?.Trim(),
            Notes = null,
            IsDeleted = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.People.Add(createdPerson);
        await db.SaveChangesAsync(cancellationToken);

        personCache[cacheKey] = createdPerson;
        return new PersonResolution(
            createdPerson,
            true,
            warningParts.Count == 0 ? null : string.Join(" ", warningParts));
    }

    private async Task<Registration> ResolveRegistrationAsync(
        ApplicationDbContext db,
        HistoricalImportRow? sourceRow,
        ParsedHistoricalRow parsedRow,
        Dictionary<string, Registration> registrationCache,
        RegistrationSubmission submission,
        Person person,
        Dictionary<string, Kingdom> kingdomCache,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var kingdom = await GetOrCreateKingdomAsync(
            db,
            kingdomCache,
            parsedRow.PreferredKingdomName,
            cancellationToken);

        if (sourceRow?.LinkedRegistrationId is int linkedRegistrationId)
        {
            var linkedRegistration = await db.Registrations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == linkedRegistrationId, cancellationToken);

            if (linkedRegistration is not null)
            {
                MergeRegistration(linkedRegistration, parsedRow, kingdom, nowUtc);
                return linkedRegistration;
            }
        }

        var cacheKey = $"{submission.Id}|{person.Id}";
        if (!registrationCache.TryGetValue(cacheKey, out var registration))
        {
            registration = await db.Registrations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    x => x.SubmissionId == submission.Id && x.PersonId == person.Id,
                    cancellationToken)
                ?? new Registration
                {
                    SubmissionId = submission.Id,
                    PersonId = person.Id,
                    AttendeeType = parsedRow.AttendeeType,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };

            if (registration.Id == 0)
            {
                db.Registrations.Add(registration);
                await db.SaveChangesAsync(cancellationToken);
            }

            registrationCache[cacheKey] = registration;
        }

        MergeRegistration(registration, parsedRow, kingdom, nowUtc);
        return registration;
    }

    private async Task<CharacterResolution> ResolveCharacterAsync(
        ApplicationDbContext db,
        HistoricalImportRow? sourceRow,
        ParsedHistoricalRow parsedRow,
        Dictionary<string, Character> characterCache,
        Game game,
        Registration registration,
        Person person,
        Dictionary<string, Kingdom> kingdomCache,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var characterName = parsedRow.CharacterName?.Trim();
        if (string.IsNullOrWhiteSpace(characterName))
        {
            return new CharacterResolution(null, false, null);
        }

        if (sourceRow?.LinkedCharacterId is int linkedCharacterId)
        {
            var linkedCharacter = await db.Characters.FirstOrDefaultAsync(x => x.Id == linkedCharacterId, cancellationToken);
            if (linkedCharacter is not null)
            {
                await EnsureCharacterAppearanceAsync(
                    db,
                    linkedCharacter,
                    game,
                    registration,
                    parsedRow,
                    kingdomCache,
                    nowUtc,
                    cancellationToken);
                return new CharacterResolution(linkedCharacter, false, null);
            }
        }

        var characterKey = $"{person.Id}|{NormalizeComparisonText(characterName)}";
        if (!characterCache.TryGetValue(characterKey, out var character))
        {
            character = await db.Characters
                .FirstOrDefaultAsync(
                    x => x.PersonId == person.Id && x.Name == characterName,
                    cancellationToken)
                ?? new Character
                {
                    PersonId = person.Id,
                    Name = characterName,
                    IsDeleted = false
                };

            var wasCreated = character.Id == 0;
            if (wasCreated)
            {
                db.Characters.Add(character);
                await db.SaveChangesAsync(cancellationToken);
            }

            characterCache[characterKey] = character;

            await EnsureCharacterAppearanceAsync(
                db,
                character,
                game,
                registration,
                parsedRow,
                kingdomCache,
                nowUtc,
                cancellationToken);

            return new CharacterResolution(character, wasCreated, null);
        }

        await EnsureCharacterAppearanceAsync(
            db,
            character,
            game,
            registration,
            parsedRow,
            kingdomCache,
            nowUtc,
            cancellationToken);

        return new CharacterResolution(character, false, null);
    }

    private async Task EnsureCharacterAppearanceAsync(
        ApplicationDbContext db,
        Character character,
        Game game,
        Registration registration,
        ParsedHistoricalRow parsedRow,
        Dictionary<string, Kingdom> kingdomCache,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var appearance = await db.CharacterAppearances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.CharacterId == character.Id && x.GameId == game.Id,
                cancellationToken);

        var kingdom = await GetOrCreateKingdomAsync(
            db,
            kingdomCache,
            parsedRow.PreferredKingdomName,
            cancellationToken);

        if (appearance is null)
        {
            appearance = new CharacterAppearance
            {
                CharacterId = character.Id,
                GameId = game.Id,
                RegistrationId = registration.Id,
                AssignedKingdomId = kingdom?.Id,
                ContinuityStatus = ContinuityStatus.Unknown,
                Notes = parsedRow.Note?.Trim()
            };

            db.CharacterAppearances.Add(appearance);
        }
        else
        {
            appearance.RegistrationId ??= registration.Id;
            appearance.AssignedKingdomId ??= kingdom?.Id;
            appearance.Notes = AppendUniqueText(appearance.Notes, parsedRow.Note);
        }
    }

    private async Task<Kingdom?> GetOrCreateKingdomAsync(
        ApplicationDbContext db,
        Dictionary<string, Kingdom> kingdomCache,
        string? preferredKingdomName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(preferredKingdomName))
        {
            return null;
        }

        var normalizedDisplayName = NormalizeComparisonText(preferredKingdomName);
        if (kingdomCache.TryGetValue(normalizedDisplayName, out var cachedKingdom))
        {
            return cachedKingdom;
        }

        var slug = BuildKingdomSlug(preferredKingdomName);
        while (await db.Kingdoms.AnyAsync(x => x.Name == slug, cancellationToken))
        {
            slug = $"{slug}-{RandomNumberGenerator.GetInt32(100, 999)}";
        }

        var kingdom = new Kingdom
        {
            Name = slug,
            DisplayName = preferredKingdomName.Trim()
        };

        db.Kingdoms.Add(kingdom);
        await db.SaveChangesAsync(cancellationToken);
        kingdomCache[normalizedDisplayName] = kingdom;
        return kingdom;
    }

    private static void UpsertSourceRow(
        ApplicationDbContext db,
        Dictionary<string, HistoricalImportRow> sourceRowMap,
        int batchId,
        ParsedHistoricalRow parsedRow,
        HistoricalImportRow? existingRow,
        int personId,
        int submissionId,
        int registrationId,
        int? characterId,
        string? warningMessage,
        DateTime nowUtc)
    {
        if (existingRow is null)
        {
            existingRow = new HistoricalImportRow
            {
                LastBatchId = batchId,
                SourceFormat = parsedRow.SourceFormat,
                SourceSheet = parsedRow.SourceSheet,
                SourceKey = parsedRow.SourceKey,
                SourceLabel = parsedRow.SourceLabel,
                LinkedPersonId = personId,
                LinkedSubmissionId = submissionId,
                LinkedRegistrationId = registrationId,
                LinkedCharacterId = characterId,
                WarningMessage = warningMessage,
                FirstImportedAtUtc = nowUtc,
                LastImportedAtUtc = nowUtc
            };

            db.HistoricalImportRows.Add(existingRow);
            sourceRowMap[BuildSourceLookupKey(parsedRow.SourceSheet, parsedRow.SourceKey)] = existingRow;
            return;
        }

        existingRow.LastBatchId = batchId;
        existingRow.SourceLabel = parsedRow.SourceLabel;
        existingRow.LinkedPersonId = personId;
        existingRow.LinkedSubmissionId = submissionId;
        existingRow.LinkedRegistrationId = registrationId;
        existingRow.LinkedCharacterId = characterId;
        existingRow.WarningMessage = warningMessage;
        existingRow.LastImportedAtUtc = nowUtc;
    }

    private static void MergeRegistration(
        Registration registration,
        ParsedHistoricalRow parsedRow,
        Kingdom? kingdom,
        DateTime nowUtc)
    {
        if (registration.AttendeeType != AttendeeType.Player || parsedRow.AttendeeType == AttendeeType.Player)
        {
            registration.AttendeeType = parsedRow.AttendeeType;
        }

        registration.PlayerSubType ??= parsedRow.PlayerSubType;
        registration.AdultRoles |= parsedRow.AdultRoles;
        registration.CharacterName ??= parsedRow.CharacterName?.Trim();
        registration.ContactEmail ??= parsedRow.Email?.Trim();
        registration.ContactPhone ??= parsedRow.Phone?.Trim();
        registration.GuardianName ??= parsedRow.GuardianName?.Trim();
        registration.GuardianRelationship ??= string.IsNullOrWhiteSpace(parsedRow.GuardianName) ? null : "zákonný zástupce";
        registration.RegistrantNote = AppendUniqueText(registration.RegistrantNote, parsedRow.Note);
        registration.PreferredKingdomId ??= kingdom?.Id;
        registration.Status = RegistrationStatus.Active;
        registration.UpdatedAtUtc = nowUtc;
    }

    private static BirthYearResolution ResolveBirthYear(string? rawAge, AttendeeType attendeeType, int eventYear)
    {
        if (string.IsNullOrWhiteSpace(rawAge))
        {
            return new BirthYearResolution(
                eventYear - (attendeeType == AttendeeType.Player ? 12 : 30),
                "Chyběl věk/ročník, proto byl doplněn odhad.");
        }

        var digits = new string(rawAge.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var numericValue))
        {
            return new BirthYearResolution(
                eventYear - (attendeeType == AttendeeType.Player ? 12 : 30),
                $"Hodnota věku/ročníku '{rawAge}' nešla bezpečně přečíst, proto byl doplněn odhad.");
        }

        var candidates = new List<(int BirthYear, int Score, string Kind)>();
        AddBirthYearCandidate(candidates, attendeeType, eventYear, numericValue, "primarni-hodnota", 0);

        if (numericValue <= 120)
        {
            AddBirthYearCandidate(candidates, attendeeType, eventYear, eventYear - numericValue, "vek", 4);
        }

        var lastTwoDigits = numericValue % 100;
        AddBirthYearCandidate(candidates, attendeeType, eventYear, 1900 + lastTwoDigits, "dvouciferny-1900", 30);
        AddBirthYearCandidate(candidates, attendeeType, eventYear, 2000 + lastTwoDigits, "dvouciferny-2000", 20);
        AddBirthYearCandidate(candidates, attendeeType, eventYear, eventYear - lastTwoDigits, "sufix-veku", 25);

        var selected = candidates
            .OrderBy(x => x.Score)
            .ThenByDescending(x => x.BirthYear)
            .FirstOrDefault();

        if (selected.BirthYear == 0)
        {
            return new BirthYearResolution(
                eventYear - (attendeeType == AttendeeType.Player ? 12 : 30),
                $"Hodnota věku/ročníku '{rawAge}' byla mimo očekávaný rozsah, proto byl doplněn odhad.");
        }

        var warning = selected.Kind is "primarni-hodnota" or "vek"
            ? null
            : $"Hodnota věku/ročníku '{rawAge}' byla vyhodnocena jako ročník {selected.BirthYear}.";

        return new BirthYearResolution(selected.BirthYear, warning);
    }

    private static void AddBirthYearCandidate(
        List<(int BirthYear, int Score, string Kind)> candidates,
        AttendeeType attendeeType,
        int eventYear,
        int birthYear,
        string kind,
        int baseScore)
    {
        if (birthYear is < 1900 or > 2099 || birthYear > eventYear)
        {
            return;
        }

        var age = eventYear - birthYear;
        if (!IsPlausibleAge(attendeeType, age))
        {
            return;
        }

        var targetAge = attendeeType == AttendeeType.Player ? 12 : 30;
        candidates.Add((birthYear, baseScore + Math.Abs(targetAge - age), kind));
    }

    private static bool IsPlausibleAge(AttendeeType attendeeType, int age) =>
        attendeeType == AttendeeType.Player
            ? age is >= 4 and <= 25
            : age is >= 16 and <= 95;

    private static FullNameResolution SplitFullName(string rawFullName)
    {
        var parts = rawFullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => new FullNameResolution("Neznámé", "?", "Jméno na řádku chybělo."),
            1 => new FullNameResolution(parts[0], "?", $"Účastník '{rawFullName}' neměl v importu vyplněné příjmení."),
            _ => new FullNameResolution(
                string.Join(' ', parts[..^1]),
                parts[^1],
                null)
        };
    }

    private static string BuildGoogleFormSourceKey(string? timestamp, string? groupName, string fullName, string? email) =>
        string.Join(
            "|",
            NormalizeComparisonText(timestamp),
            NormalizeComparisonText(groupName),
            NormalizeComparisonText(fullName),
            NormalizeComparisonText(email));

    private static string BuildLegacySourceKey(string sheetName, string? groupName, string fullName, string? email, string? phone) =>
        string.Join(
            "|",
            NormalizeComparisonText(sheetName),
            NormalizeComparisonText(groupName),
            NormalizeComparisonText(fullName),
            NormalizeComparisonText(email),
            NormalizePhone(phone));

    private static string BuildSourceLabel(string? groupName, string fullName)
    {
        var group = groupName?.Trim();
        return string.IsNullOrWhiteSpace(group) ? fullName.Trim() : $"{group} / {fullName.Trim()}";
    }

    private static string BuildSourceLookupKey(string sheetName, string sourceKey) => $"{sheetName}|{sourceKey}";

    private static string BuildHouseholdKey(ParsedHistoricalRow parsedRow)
    {
        var email = NormalizeComparisonText(parsedRow.Email);
        var phone = NormalizePhone(parsedRow.Phone);
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(email))
        {
            parts.Add(email);
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            parts.Add(phone);
        }

        if (parts.Count == 0)
        {
            parts.Add(NormalizeComparisonText(parsedRow.GroupName));
        }

        if (parts.Count == 0)
        {
            parts.Add(NormalizeComparisonText(parsedRow.FullName));
        }

        return string.Join("|", parts);
    }

    private static string BuildHistoricalDisplayName(ParsedHistoricalRow parsedRow)
    {
        var groupName = parsedRow.GroupName?.Trim();
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            return $"Historický import: {groupName}";
        }

        return $"Historický import: {parsedRow.FullName.Trim()}";
    }

    private static string BuildPrimaryContactName(ParsedHistoricalRow parsedRow)
    {
        if (!string.IsNullOrWhiteSpace(parsedRow.GroupName))
        {
            return parsedRow.GroupName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(parsedRow.GuardianName))
        {
            return parsedRow.GuardianName.Trim();
        }

        return parsedRow.FullName.Trim();
    }

    private static string BuildPersonCacheKey(
        string firstName,
        string lastName,
        int birthYear,
        string? email,
        string? phone) =>
        string.Join(
            "|",
            NormalizeComparisonText(firstName),
            NormalizeComparisonText(lastName),
            birthYear.ToString(CultureInfo.InvariantCulture),
            NormalizeEmail(email),
            NormalizePhone(phone));

    private static string BuildSurrogateEmail(int gameId, string householdKey)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{gameId}:{householdKey}")))
            .ToLowerInvariant();

        return $"historical-{gameId}-{hash[..12]}@import.ovcina.local";
    }

    private static string? NormalizeCharacterName(string? rawCharacterName, string? rawKingdomName)
    {
        if (string.IsNullOrWhiteSpace(rawCharacterName))
        {
            return null;
        }

        var candidate = rawCharacterName.Trim();
        var normalizedCandidate = NormalizeComparisonText(candidate);
        if (GenericCharacterNames.Contains(normalizedCandidate, StringComparer.Ordinal))
        {
            return null;
        }

        if (normalizedCandidate == NormalizeComparisonText(rawKingdomName))
        {
            return null;
        }

        return candidate;
    }

    private static bool IsTruthy(string? rawValue)
    {
        var normalized = NormalizeComparisonText(rawValue);
        return normalized is "true" or "1" or "ano" or "yes";
    }

    private static string GetCellText(IXLRow row, int columnIndex)
    {
        var cell = row.Cell(columnIndex);
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        return cell.GetFormattedString().Trim();
    }

    private static string NormalizeComparisonText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWhitespace = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }

                continue;
            }

            previousWhitespace = false;
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizePhone(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsDigit).ToArray());

    private static string BuildKingdomSlug(string displayName)
    {
        var normalized = NormalizeComparisonText(displayName);
        var builder = new StringBuilder(normalized.Length);
        var previousHyphen = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousHyphen = false;
            }
            else if ((character == ' ' || character == '-') && !previousHyphen)
            {
                builder.Append('-');
                previousHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "historicke-kralovstvi" : slug;
    }

    private static void HydrateSubmissionContact(RegistrationSubmission submission, ParsedHistoricalRow parsedRow)
    {
        if (string.IsNullOrWhiteSpace(submission.PrimaryContactName))
        {
            submission.PrimaryContactName = BuildPrimaryContactName(parsedRow);
        }

        if ((string.IsNullOrWhiteSpace(submission.PrimaryEmail) || submission.PrimaryEmail.EndsWith("@import.ovcina.local", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(parsedRow.Email))
        {
            submission.PrimaryEmail = parsedRow.Email.Trim();
        }

        if ((string.IsNullOrWhiteSpace(submission.PrimaryPhone) || submission.PrimaryPhone == "neuvedeno")
            && !string.IsNullOrWhiteSpace(parsedRow.Phone))
        {
            submission.PrimaryPhone = parsedRow.Phone.Trim();
        }

        submission.RegistrantNote = AppendUniqueText(submission.RegistrantNote, parsedRow.Note);
    }

    private static void HydratePersonContact(Person person, ParsedHistoricalRow parsedRow)
    {
        if (string.IsNullOrWhiteSpace(person.Email) && !string.IsNullOrWhiteSpace(parsedRow.Email))
        {
            person.Email = parsedRow.Email.Trim();
        }

        if (string.IsNullOrWhiteSpace(person.Phone) && !string.IsNullOrWhiteSpace(parsedRow.Phone))
        {
            person.Phone = parsedRow.Phone.Trim();
        }
    }

    private static string? AppendUniqueText(string? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming.Trim();
        }

        var trimmedIncoming = incoming.Trim();
        if (existing.Contains(trimmedIncoming, StringComparison.Ordinal))
        {
            return existing;
        }

        return $"{existing.Trim()}{Environment.NewLine}{trimmedIncoming}";
    }
}

public sealed record HistoricalImportPageModel(
    IReadOnlyList<HistoricalImportBatchListItem> Batches,
    HistoricalImportBatchDetails? HighlightedBatch);

public sealed record HistoricalImportBatchListItem(
    int Id,
    string Label,
    string SourceFormat,
    string SourceFileName,
    string GameName,
    DateTime ImportedAtUtc,
    int TotalSourceRows,
    int HouseholdCount,
    int RegistrationCount,
    int PersonCreatedCount,
    int PersonMatchedCount,
    int CharacterCreatedCount,
    int WarningCount);

public sealed record HistoricalImportBatchDetails(
    int Id,
    string Label,
    string SourceFormat,
    string SourceFileName,
    string GameName,
    DateTime ImportedAtUtc,
    int TotalSourceRows,
    int HouseholdCount,
    int RegistrationCount,
    int PersonCreatedCount,
    int PersonMatchedCount,
    int CharacterCreatedCount,
    int WarningCount,
    IReadOnlyList<HistoricalImportWarningItem> Warnings);

public sealed record HistoricalImportWarningItem(
    string SourceSheet,
    string SourceLabel,
    string Message);

public sealed record HistoricalImportResult(int BatchId);

internal sealed record ParsedHistoricalRow(
    string SourceFormat,
    string SourceSheet,
    string SourceKey,
    string SourceLabel,
    string? GroupName,
    string FullName,
    string? CharacterName,
    string? GuardianName,
    string? Note,
    string? Email,
    string? Phone,
    string? PreferredKingdomName,
    DateTime? SubmittedAtUtc,
    string? RawAge,
    AttendeeType AttendeeType,
    PlayerSubType? PlayerSubType,
    AdultRoleFlags AdultRoles,
    string? Warning);

internal sealed record ParticipantTypeInfo(
    AttendeeType AttendeeType,
    PlayerSubType? PlayerSubType,
    AdultRoleFlags AdultRoles);

internal sealed record LegacyOrganizerRoleInfo(
    AdultRoleFlags AdultRoles,
    string? PreferredKingdomName);

internal sealed record BirthYearResolution(int BirthYear, string? Warning);

internal sealed record FullNameResolution(string FirstName, string LastName, string? Warning);

internal sealed record PersonResolution(Person Person, bool WasCreated, string? Warning);

internal sealed record CharacterResolution(Character? Character, bool WasCreated, string? Warning);
