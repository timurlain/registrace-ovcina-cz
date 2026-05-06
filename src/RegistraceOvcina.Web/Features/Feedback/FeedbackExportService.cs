using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Builds an XLSX export of post-game feedback responses for an entire game.
/// Mirrors the pattern established by
/// <see cref="CharacterPrep.CharacterPrepExportService"/>: a single ClosedXML
/// workbook returned as a byte array, with the HTTP endpoint (see Program.cs)
/// wrapping it in <c>Results.File(...)</c>.
/// </summary>
/// <remarks>
/// <para>The workbook always contains exactly two sheets: "Děti" (kid /
/// <see cref="AttendeeType.Player"/> attendees) and "Dospělí"
/// (<see cref="AttendeeType.Adult"/> attendees). Each sheet has a fixed set of
/// metadata columns followed by one column per question in the per-game
/// question set. When the game has no data, both sheets are still written —
/// only the headers are populated.</para>
/// <para>Rows include any registration that has either a saved
/// <see cref="FeedbackResponse"/> or a non-null
/// <see cref="Registration.FeedbackInvitedAtUtc"/> (= "was invited"). Soft-
/// deleted submissions are skipped by the EF Core query filter on
/// <see cref="RegistrationSubmission"/>.</para>
/// </remarks>
public sealed class FeedbackExportService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    FeedbackOptionsService optionsService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string KidSheetName = "Děti";
    private const string AdultSheetName = "Dospělí";

    /// <summary>
    /// Produces an .xlsx workbook with two sheets ("Děti" + "Dospělí"), one
    /// row per attendee that has a feedback response or was invited, and one
    /// column per question in the corresponding question set.
    /// </summary>
    public async Task<byte[]> ExportAsync(int gameId, CancellationToken ct)
    {
        var (kidQuestions, adultQuestions) = await optionsService
            .GetBothSetsAsync(gameId, ct);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Single query, project everything we need into a flat row. The query
        // filter on RegistrationSubmission already drops soft-deleted ones.
        var rows = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Submission.GameId == gameId)
            .Where(r => r.FeedbackResponse != null || r.FeedbackInvitedAtUtc != null)
            .Select(r => new ExportRow(
                r.Person.FirstName,
                r.Person.LastName,
                r.Submission.PrimaryContactName,
                r.CharacterName,
                r.AttendeeType,
                r.FeedbackInvitedAtUtc,
                r.FeedbackResponse != null ? r.FeedbackResponse.SubmittedAtUtc : null,
                r.FeedbackResponse != null ? r.FeedbackResponse.AnswersJson : null,
                r.FeedbackResponse != null ? (FeedbackEditedBy?)r.FeedbackResponse.LastEditedBy : null,
                r.FeedbackResponse != null && r.FeedbackResponse.PinToChronicle,
                r.FeedbackResponse != null ? r.FeedbackResponse.OrganizerNote : null))
            .ToListAsync(ct);

        // Sort by LastName then FirstName, ordinal-ignore-case (export only —
        // Czech-cultural ordering is not load-bearing here).
        rows = rows
            .OrderBy(r => r.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var workbook = new XLWorkbook();
        WriteSheet(
            workbook.AddWorksheet(KidSheetName),
            rows.Where(r => r.AttendeeType == AttendeeType.Player).ToList(),
            kidQuestions);
        WriteSheet(
            workbook.AddWorksheet(AdultSheetName),
            rows.Where(r => r.AttendeeType == AttendeeType.Adult).ToList(),
            adultQuestions);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSheet(
        IXLWorksheet sheet,
        IReadOnlyList<ExportRow> rows,
        FeedbackQuestionSet questionSet)
    {
        // Fixed metadata columns. Keep the labels Czech to match the rest of
        // the organizer UI / export filenames.
        string[] fixedHeaders =
        [
            "Jméno",
            "Příjmení",
            "Rodina",
            "Postava",
            "Stav",
            "Odesláno",
            "Vyplnil",
            "Pin do kroniky",
            "Poznámka organizátora",
        ];

        for (var c = 0; c < fixedHeaders.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = fixedHeaders[c];
        }

        var firstQuestionCol = fixedHeaders.Length + 1;
        var questions = questionSet.Questions;
        for (var qi = 0; qi < questions.Count; qi++)
        {
            sheet.Cell(1, firstQuestionCol + qi).Value = questions[qi].Label;
        }

        var totalCols = fixedHeaders.Length + questions.Count;

        // Bold + freeze header row.
        if (totalCols > 0)
        {
            sheet.Range(1, 1, 1, totalCols).Style.Font.Bold = true;
        }

        sheet.SheetView.FreezeRows(1);

        // Write data rows.
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var xlRow = r + 2;

            sheet.Cell(xlRow, 1).Value = row.FirstName;
            sheet.Cell(xlRow, 2).Value = row.LastName;
            sheet.Cell(xlRow, 3).Value = row.Household;

            if (!string.IsNullOrEmpty(row.CharacterName))
            {
                sheet.Cell(xlRow, 4).Value = row.CharacterName;
            }

            sheet.Cell(xlRow, 5).Value = StatusLabel(row.InvitedAtUtc, row.SubmittedAtUtc);

            if (row.SubmittedAtUtc is { } submittedUtc)
            {
                sheet.Cell(xlRow, 6).Value = ToCzechLocalDateTime(submittedUtc);
            }

            if (row.LastEditedBy is { } editedBy)
            {
                sheet.Cell(xlRow, 7).Value = EditedByLabel(editedBy);
            }

            if (row.PinToChronicle)
            {
                sheet.Cell(xlRow, 8).Value = "Ano";
            }

            if (!string.IsNullOrEmpty(row.OrganizerNote))
            {
                sheet.Cell(xlRow, 9).Value = row.OrganizerNote;
            }

            // Per-question answers.
            var answers = DeserializeAnswers(row.AnswersJson);
            for (var qi = 0; qi < questions.Count; qi++)
            {
                if (answers.TryGetValue(questions[qi].Key, out var value)
                    && !string.IsNullOrEmpty(value))
                {
                    sheet.Cell(xlRow, firstQuestionCol + qi).Value = value;
                }
            }
        }

        // Wrap text on every body cell so long answers stay readable.
        if (rows.Count > 0 && totalCols > 0)
        {
            sheet.Range(2, 1, rows.Count + 1, totalCols)
                .Style.Alignment.WrapText = true;
        }

        // Auto-size columns, capped at ~80 chars to keep the file usable when
        // a single answer is enormous.
        if (totalCols > 0)
        {
            sheet.Columns().AdjustToContents(1, rows.Count + 1, 8, 80);
        }
    }

    private static IReadOnlyDictionary<string, string> DeserializeAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EmptyAnswers;
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return (IReadOnlyDictionary<string, string>?)dict ?? EmptyAnswers;
        }
        catch (JsonException)
        {
            // Tolerant: a malformed legacy blob should not blow up the export.
            return EmptyAnswers;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyAnswers =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static DateTime ToCzechLocalDateTime(DateTimeOffset utc)
    {
        // Project memory: "OvčinaHra times — UTC wire, local Czech display +
        // exports". Use the Windows TZ id; on non-Windows runtimes the
        // hosted environment uses the IANA fallback so this is safe.
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
            return TimeZoneInfo.ConvertTime(utc, tz).DateTime;
        }
        catch (TimeZoneNotFoundException)
        {
            return utc.LocalDateTime;
        }
    }

    private static string StatusLabel(
        DateTimeOffset? invitedAtUtc,
        DateTimeOffset? submittedAtUtc)
    {
        if (submittedAtUtc is not null)
        {
            return "Hotovo";
        }
        return invitedAtUtc is not null ? "Čeká" : "Nezváno";
    }

    private static string EditedByLabel(FeedbackEditedBy editedBy) => editedBy switch
    {
        FeedbackEditedBy.Self => "Sám",
        FeedbackEditedBy.HouseholdContact => "Rodič",
        FeedbackEditedBy.Organizer => "Organizátor",
        _ => "—",
    };

    private sealed record ExportRow(
        string FirstName,
        string LastName,
        string Household,
        string? CharacterName,
        AttendeeType AttendeeType,
        DateTimeOffset? InvitedAtUtc,
        DateTimeOffset? SubmittedAtUtc,
        string? AnswersJson,
        FeedbackEditedBy? LastEditedBy,
        bool PinToChronicle,
        string? OrganizerNote);
}
