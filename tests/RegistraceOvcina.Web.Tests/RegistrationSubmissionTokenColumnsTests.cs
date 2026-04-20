using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Tests;

public sealed class RegistrationSubmissionTokenColumnsTests
{
    [Fact]
    public void CharacterPrepToken_HasMaxLength64AndIsNullable()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(RegistrationSubmission))!;

        var property = entityType.FindProperty(nameof(RegistrationSubmission.CharacterPrepToken))!;

        Assert.Equal(64, property.GetMaxLength());
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void CharacterPrepInvitedAtUtc_IsNullableDateTimeOffset()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(RegistrationSubmission))!;

        var property = entityType.FindProperty(nameof(RegistrationSubmission.CharacterPrepInvitedAtUtc))!;

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        Assert.Equal(typeof(DateTimeOffset?), property.ClrType);
    }

    [Fact]
    public void CharacterPrepReminderLastSentAtUtc_IsNullableDateTimeOffset()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(RegistrationSubmission))!;

        var property = entityType.FindProperty(nameof(RegistrationSubmission.CharacterPrepReminderLastSentAtUtc))!;

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        Assert.Equal(typeof(DateTimeOffset?), property.ClrType);
    }

    [Fact]
    public void UniqueFilteredIndex_On_CharacterPrepToken_Exists()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(RegistrationSubmission))!;

        var tokenIndex = entityType.GetIndexes().SingleOrDefault(i =>
            i.IsUnique &&
            i.Properties.Count == 1 &&
            i.Properties[0].Name == nameof(RegistrationSubmission.CharacterPrepToken));

        Assert.NotNull(tokenIndex);
        var filter = tokenIndex.GetFilter();
        Assert.NotNull(filter);
        Assert.Equal("\"CharacterPrepToken\" IS NOT NULL", filter);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }
}
