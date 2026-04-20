using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Builds an XLSX export of character-prep rows for an entire game. Mirrors
/// the pattern established by <see cref="Kingdoms.KingdomExportService"/>:
/// a single ClosedXML workbook returned as a byte array, with the HTTP
/// endpoint (see Program.cs) wrapping it in <c>Results.File(...)</c>.
/// </summary>
public sealed class CharacterPrepExportService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<byte[]> BuildAsync(int gameId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Game is loaded primarily so the caller (endpoint) can use Name for
        // the filename slug; keeping it out of the sheet keeps the layout
        // simple and focused on one household per row.
        _ = await db.Games
            .AsNoTracking()
            .Where(x => x.Id == gameId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(ct);

        var rows = await db.Registrations
            .AsNoTracking()
            .Where(x => x.Submission.GameId == gameId && x.AttendeeType == AttendeeType.Player)
            .OrderBy(x => x.Person.LastName)
            .ThenBy(x => x.Person.FirstName)
            .Select(x => new ExportRow(
                x.Person.FirstName + " " + x.Person.LastName,
                x.Person.BirthYear,
                x.CharacterName,
                x.StartingEquipmentOption != null ? x.StartingEquipmentOption.DisplayName : null,
                x.CharacterPrepNote,
                x.Submission.PrimaryContactName,
                x.Submission.PrimaryEmail))
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Příprava postav");

        string[] headers =
        [
            "Hráč",
            "Rok narození",
            "Jméno postavy",
            "Startovní výbava",
            "Poznámka",
            "Domácnost",
            "Email domácnosti",
        ];

        for (var h = 0; h < headers.Length; h++)
        {
            sheet.Cell(1, h + 1).Value = headers[h];
            sheet.Cell(1, h + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var xlRow = r + 2;

            sheet.Cell(xlRow, 1).Value = row.PersonFullName;
            sheet.Cell(xlRow, 2).Value = row.BirthYear;

            // Empty cells stay truly empty — do not write "" or "—" placeholders.
            if (!string.IsNullOrEmpty(row.CharacterName))
            {
                sheet.Cell(xlRow, 3).Value = row.CharacterName;
            }

            if (!string.IsNullOrEmpty(row.EquipmentDisplayName))
            {
                sheet.Cell(xlRow, 4).Value = row.EquipmentDisplayName;
            }

            if (!string.IsNullOrEmpty(row.CharacterPrepNote))
            {
                sheet.Cell(xlRow, 5).Value = row.CharacterPrepNote;
            }

            sheet.Cell(xlRow, 6).Value = row.PrimaryContactName;
            sheet.Cell(xlRow, 7).Value = row.PrimaryEmail;
        }

        // Formatting: frozen header, autofilter on header, auto-width capped at 40 chars.
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()!.SetAutoFilter();
        sheet.Columns().AdjustToContents(1, rows.Count + 1, 8, 40);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record ExportRow(
        string PersonFullName,
        int BirthYear,
        string? CharacterName,
        string? EquipmentDisplayName,
        string? CharacterPrepNote,
        string PrimaryContactName,
        string PrimaryEmail);
}
