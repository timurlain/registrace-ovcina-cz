using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Tests;

public sealed class StartingEquipmentOptionConfigurationTests
{
    [Fact]
    public void StartingEquipmentOption_IsRegisteredAsEntity()
    {
        using var db = CreateDb();

        var entityType = db.Model.FindEntityType(typeof(StartingEquipmentOption));

        Assert.NotNull(entityType);
    }

    [Fact]
    public void StartingEquipmentOption_HasDbSetOnContext()
    {
        using var db = CreateDb();

        Assert.NotNull(db.StartingEquipmentOptions);
    }

    [Fact]
    public void Key_HasMaxLength50AndIsRequired()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(StartingEquipmentOption))!;

        var property = entityType.FindProperty(nameof(StartingEquipmentOption.Key))!;

        Assert.Equal(50, property.GetMaxLength());
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void DisplayName_HasMaxLength100AndIsRequired()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(StartingEquipmentOption))!;

        var property = entityType.FindProperty(nameof(StartingEquipmentOption.DisplayName))!;

        Assert.Equal(100, property.GetMaxLength());
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void Description_HasMaxLength500AndIsNullable()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(StartingEquipmentOption))!;

        var property = entityType.FindProperty(nameof(StartingEquipmentOption.Description))!;

        Assert.Equal(500, property.GetMaxLength());
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void UniqueIndex_On_GameId_Key_Exists()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(StartingEquipmentOption))!;

        var uniqueComposite = entityType.GetIndexes().SingleOrDefault(i =>
            i.IsUnique &&
            i.Properties.Count == 2 &&
            i.Properties[0].Name == nameof(StartingEquipmentOption.GameId) &&
            i.Properties[1].Name == nameof(StartingEquipmentOption.Key));

        Assert.NotNull(uniqueComposite);
    }

    [Fact]
    public void ForeignKey_ToGame_IsCascade()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(StartingEquipmentOption))!;

        var fk = entityType.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(Game));

        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }
}
