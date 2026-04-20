using System.Text.RegularExpressions;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Lock-in test for the organizer-dashboard household link. Regression guard for
/// a Copilot-flagged bug where the dashboard linked to <c>/organizace/prihlaska/…</c>
/// (singular) while the real SubmissionDetail route is <c>/organizace/prihlasky/…</c>
/// (plural), giving a 404. This test reads the two razor files from disk and asserts
/// the dashboard href matches the plural route directive in SubmissionDetail.
/// </summary>
public sealed class CharacterPrepDashboardLinkTests
{
    // Walk up from the test binary to the repo root and then down to the razor files.
    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "RegistraceOvcina.Web");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/RegistraceOvcina.Web relative to the test binary.");
    }

    [Fact]
    public void Dashboard_household_link_uses_plural_prihlasky_matching_SubmissionDetail_route()
    {
        var webRoot = FindWebRoot();
        var dashboardPath = Path.Combine(
            webRoot, "Components", "Pages", "Organizer", "CharacterPrepDashboard.razor");
        var detailPath = Path.Combine(
            webRoot, "Components", "Pages", "Organizer", "SubmissionDetail.razor");

        Assert.True(File.Exists(dashboardPath), $"Missing: {dashboardPath}");
        Assert.True(File.Exists(detailPath), $"Missing: {detailPath}");

        var dashboard = File.ReadAllText(dashboardPath);
        var detail = File.ReadAllText(detailPath);

        // 1. Extract the @page route from SubmissionDetail.razor — the source of truth.
        var routeMatch = Regex.Match(
            detail,
            "@page\\s+\"(?<route>/organizace/prihlasky/\\{submissionId:int\\})\"",
            RegexOptions.IgnoreCase);
        Assert.True(
            routeMatch.Success,
            "SubmissionDetail.razor must declare @page \"/organizace/prihlasky/{submissionId:int}\". "
            + "If the route changed, update CharacterPrepDashboard.razor and this test together.");

        // 2. Dashboard row-link must target the plural segment.
        Assert.Contains(
            "href=\"/organizace/prihlasky/@row.SubmissionId\"",
            dashboard);

        // 3. Bug lock: the singular variant must never reappear anywhere in the dashboard.
        Assert.DoesNotContain("/organizace/prihlaska/", dashboard);
    }
}
