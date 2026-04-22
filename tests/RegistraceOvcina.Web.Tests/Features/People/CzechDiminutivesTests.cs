using RegistraceOvcina.Web.Features.People;

namespace RegistraceOvcina.Web.Tests.Features.People;

public sealed class CzechDiminutivesTests
{
    [Fact]
    public void AreEquivalent_identical_returns_true()
    {
        Assert.True(CzechDiminutives.AreEquivalent("jan", "jan"));
    }

    [Fact]
    public void Jan_Honza_equivalent()
    {
        Assert.True(CzechDiminutives.AreEquivalent("jan", "honza"));
        Assert.True(CzechDiminutives.AreEquivalent("honza", "jan"));
    }

    [Fact]
    public void Petr_Pavel_not_equivalent()
    {
        Assert.False(CzechDiminutives.AreEquivalent("petr", "pavel"));
    }

    [Fact]
    public void Katerina_Katka_equivalent()
    {
        Assert.True(CzechDiminutives.AreEquivalent("katerina", "katka"));
    }
}
