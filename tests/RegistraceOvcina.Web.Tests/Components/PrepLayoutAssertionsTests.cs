using System;
using System.IO;
using System.Linq;

namespace RegistraceOvcina.Web.Tests.Components;

/// <summary>
/// Static source-text assertions guarding the anonymous /postavy/{token} chrome.
///
/// Parents reaching CharacterPrep via the Pozvánka email link must not see a
/// "Přihlásit se" button — GitHub issue #163. These tests are crude file-text
/// checks (no bUnit dependency) but reliably catch regressions where someone
/// re-adds a login CTA to PrepLayout or drops the @layout directive on the
/// CharacterPrep page.
/// </summary>
public sealed class PrepLayoutAssertionsTests
{
    private static string RepoRoot =>
        LocateRepoRoot(AppContext.BaseDirectory);

    private static string LocateRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RegistraceOvcina.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (RegistraceOvcina.slnx) starting from '{start}'.");
    }

    private static string ReadText(params string[] relativeSegments)
    {
        var full = Path.Combine(new[] { RepoRoot }.Concat(relativeSegments).ToArray());
        Assert.True(File.Exists(full), $"Expected file to exist: {full}");
        return File.ReadAllText(full);
    }

    [Fact]
    public void PrepLayout_DoesNotContain_LoginCtaText()
    {
        var text = ReadText("src", "RegistraceOvcina.Web", "Components", "Layout", "PrepLayout.razor");

        Assert.DoesNotContain("Přihlásit", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepLayout_DoesNotReuse_NavMenu()
    {
        var text = ReadText("src", "RegistraceOvcina.Web", "Components", "Layout", "PrepLayout.razor");

        Assert.DoesNotContain("NavMenu", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepLayout_InheritsLayoutComponentBase_AndRendersBody()
    {
        var text = ReadText("src", "RegistraceOvcina.Web", "Components", "Layout", "PrepLayout.razor");

        Assert.Contains("@inherits LayoutComponentBase", text, StringComparison.Ordinal);
        Assert.Contains("@Body", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterPrepPage_OptsIntoPrepLayout()
    {
        var text = ReadText("src", "RegistraceOvcina.Web", "Components", "Pages", "CharacterPrep", "CharacterPrep.razor");

        Assert.Contains("@layout PrepLayout", text, StringComparison.Ordinal);
    }
}
