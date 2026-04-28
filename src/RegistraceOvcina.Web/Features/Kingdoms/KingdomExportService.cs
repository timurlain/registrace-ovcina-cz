using ClosedXML.Excel;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Kingdoms;

public sealed class KingdomExportService
{
    public byte[] BuildXlsx(AssignmentBoard board)
    {
        using var workbook = new XLWorkbook();
        var currentYear = DateTime.UtcNow.Year;

        // --- Sheet 1: Přehled (5-column overview, one cell per player) ---
        var overviewSheet = workbook.AddWorksheet("Přehled");

        var allColumns = new List<(string Title, List<PlayerCard> Players)>();

        foreach (var kingdom in board.Kingdoms)
        {
            allColumns.Add((kingdom.DisplayName, kingdom.Players));
        }

        allColumns.Add(("Nepřidělení", board.UnassignedPlayers));

        for (var col = 0; col < allColumns.Count; col++)
        {
            var (title, players) = allColumns[col];
            var xlCol = col + 1;

            overviewSheet.Cell(1, xlCol).Value = $"{title} ({players.Count})";
            overviewSheet.Cell(1, xlCol).Style.Font.Bold = true;
            overviewSheet.Cell(1, xlCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (var row = 0; row < players.Count; row++)
            {
                var p = players[row];
                overviewSheet.Cell(row + 2, xlCol).Value =
                    $"{p.PersonName}, {p.Age(currentYear)} let" +
                    (!string.IsNullOrWhiteSpace(p.GroupName) ? $", {p.GroupName}" : "");
            }

            overviewSheet.Column(xlCol).AdjustToContents(1, players.Count + 1, 15, 45);
        }

        // --- Sheets 2+: one per kingdom + Nepřidělení, values split by columns ---
        foreach (var (title, players) in allColumns)
        {
            var sheetName = title.Length > 31 ? title[..31] : title;
            var sheet = workbook.AddWorksheet(sheetName);

            var headers = new[]
            {
                "Jméno", "Příjmení", "Věk", "Kategorie", "Skupina", "Preference",
                "Postava", "Poznámka hráče", "Poznámka skupiny",
                "Org. poznámky", "Zákonný zástupce", "Předchozí království"
            };

            for (var h = 0; h < headers.Length; h++)
            {
                sheet.Cell(1, h + 1).Value = headers[h];
                sheet.Cell(1, h + 1).Style.Font.Bold = true;
            }

            for (var row = 0; row < players.Count; row++)
            {
                var p = players[row];
                var nameParts = p.PersonName.Split(' ', 2);
                var firstName = nameParts[0];
                var lastName = nameParts.Length > 1 ? nameParts[1] : "";
                var xlRow = row + 2;

                sheet.Cell(xlRow, 1).Value = firstName;
                sheet.Cell(xlRow, 2).Value = lastName;
                sheet.Cell(xlRow, 3).Value = p.Age(currentYear);
                sheet.Cell(xlRow, 4).Value = PlayerSubTypeLabel(p.PlayerSubType);
                sheet.Cell(xlRow, 5).Value = p.GroupName ?? "";
                sheet.Cell(xlRow, 6).Value = p.PreferredKingdomName ?? "";
                sheet.Cell(xlRow, 7).Value = p.CharacterName ?? "";
                sheet.Cell(xlRow, 8).Value = p.RegistrantNote ?? "";
                sheet.Cell(xlRow, 9).Value = p.SubmissionNote ?? "";
                sheet.Cell(xlRow, 10).Value = p.OrganizerNotes ?? "";
                sheet.Cell(xlRow, 11).Value = p.GuardianName ?? "";
                sheet.Cell(xlRow, 12).Value = p.PreviousKingdomName ?? "";
            }

            sheet.Columns().AdjustToContents(1, players.Count + 1, 8, 40);

            if (players.Count > 0)
            {
                sheet.RangeUsed()!.SetAutoFilter();
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string PlayerSubTypeLabel(PlayerSubType? subType) => subType switch
    {
        PlayerSubType.Pvp => "PVP hráč (10+)",
        PlayerSubType.Independent => "Samostatné dítě (8+)",
        PlayerSubType.WithRanger => "S hraničářem (5–7)",
        PlayerSubType.WithParent => "S rodičem (4+)",
        _ => ""
    };
}
