using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Tests;

public sealed class RegistrationCharacterPrepColumnsTests
{
    [Fact]
    public void StartingEquipmentOptionId_IsNullableFkProperty()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Registration))!;

        var property = entityType.FindProperty(nameof(Registration.StartingEquipmentOptionId))!;

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void StartingEquipmentOption_NavigationProperty_Exists()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Registration))!;

        var navigation = entityType.FindNavigation(nameof(Registration.StartingEquipmentOption));

        Assert.NotNull(navigation);
        Assert.Equal(typeof(StartingEquipmentOption), navigation.ClrType);
    }

    [Fact]
    public void CharacterPrepNote_HasMaxLength4000AndIsNullable()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Registration))!;

        var property = entityType.FindProperty(nameof(Registration.CharacterPrepNote))!;

        Assert.Equal(4000, property.GetMaxLength());
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void CharacterPrepUpdatedAtUtc_IsNullableDateTimeOffset()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Registration))!;

        var property = entityType.FindProperty(nameof(Registration.CharacterPrepUpdatedAtUtc))!;

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        Assert.Equal(typeof(DateTimeOffset?), property.ClrType);
    }

    [Fact]
    public void ForeignKey_To_StartingEquipmentOption_IsRestrict()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(Registration))!;

        var fk = entityType.GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(StartingEquipmentOption));

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }
}
