using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RegistraceOvcina.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterAppearance> CharacterAppearances => Set<CharacterAppearance>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<FoodOrder> FoodOrders => Set<FoodOrder>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameRoom> GameRooms => Set<GameRoom>();
    public DbSet<GameRole> GameRoles => Set<GameRole>();
    public DbSet<GameInvitation> GameInvitations => Set<GameInvitation>();
    public DbSet<GameKingdomTarget> GameKingdomTargets => Set<GameKingdomTarget>();
    public DbSet<HistoricalImportBatch> HistoricalImportBatches => Set<HistoricalImportBatch>();
    public DbSet<HistoricalImportRow> HistoricalImportRows => Set<HistoricalImportRow>();
    public DbSet<Kingdom> Kingdoms => Set<Kingdom>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<MealOption> MealOptions => Set<MealOption>();
    public DbSet<OrganizerNote> OrganizerNotes => Set<OrganizerNote>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RegistrationSubmission> RegistrationSubmissions => Set<RegistrationSubmission>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementDismissal> AnnouncementDismissals => Set<AnnouncementDismissal>();
    public DbSet<ExternalContact> ExternalContacts => Set<ExternalContact>();
    public DbSet<UserEmail> UserEmails => Set<UserEmail>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.UseOpenIddict();

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.PersonId);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
        });

        builder.Entity<Person>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasIndex(x => new { x.LastName, x.FirstName, x.BirthYear });
            entity.HasIndex(x => x.Email)
                .IsUnique()
                .HasFilter("\"Email\" IS NOT NULL AND \"Email\" != ''");
        });

        builder.Entity<Game>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.BankAccount).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BankAccountName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.VariableSymbolStrategy).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.PlayerBasePrice).HasPrecision(18, 2);
            entity.Property(x => x.SecondChildPrice).HasPrecision(18, 2);
            entity.Property(x => x.ThirdPlusChildPrice).HasPrecision(18, 2);
            entity.Property(x => x.AdultHelperBasePrice).HasPrecision(18, 2);
            entity.Property(x => x.LodgingIndoorPrice).HasPrecision(18, 2);
            entity.Property(x => x.LodgingOutdoorPrice).HasPrecision(18, 2);
            entity.HasMany(x => x.Submissions)
                .WithOne(x => x.Game)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.KingdomTargets)
                .WithOne(x => x.Game)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.MealOptions)
                .WithOne(x => x.Game)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Kingdom>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Color).HasMaxLength(32);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<GameKingdomTarget>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameId, x.KingdomId }).IsUnique();
            entity.HasOne(x => x.Kingdom)
                .WithMany()
                .HasForeignKey(x => x.KingdomId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RegistrationSubmission>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.Property(x => x.PrimaryContactName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PrimaryEmail).HasMaxLength(256).IsRequired();
            entity.Property(x => x.PrimaryPhone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ExpectedTotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.VoluntaryDonation).HasPrecision(18, 2);
            entity.Property(x => x.RegistrantNote).HasMaxLength(4000);
            entity.Property(x => x.PaymentVariableSymbol).HasMaxLength(20);
            entity.HasIndex(x => new { x.GameId, x.RegistrantUserId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.RegistrantUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Registrations)
                .WithOne(x => x.Submission)
                .HasForeignKey(x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Payments)
                .WithOne(x => x.Submission)
                .HasForeignKey(x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.OrganizerNotes)
                .WithOne(x => x.Submission)
                .HasForeignKey(x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Registration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.Person.IsDeleted && !x.Submission.IsDeleted);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ContactEmail).HasMaxLength(256);
            entity.Property(x => x.ContactPhone).HasMaxLength(40);
            entity.Property(x => x.GuardianName).HasMaxLength(200);
            entity.Property(x => x.GuardianRelationship).HasMaxLength(100);
            entity.Property(x => x.AttendeeType).HasDefaultValue(AttendeeType.Player);
            entity.Property(x => x.AdultRoles).HasDefaultValue(AdultRoleFlags.None);
            entity.Property(x => x.CharacterName).HasMaxLength(200);
            entity.Property(x => x.RegistrantNote).HasMaxLength(4000);
            entity.HasOne(x => x.Person)
                .WithMany(x => x.Registrations)
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PreferredKingdom)
                .WithMany()
                .HasForeignKey(x => x.PreferredKingdomId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AssignedGameRoom)
                .WithMany()
                .HasForeignKey(x => x.AssignedGameRoomId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(x => x.FoodOrders)
                .WithOne(x => x.Registration)
                .HasForeignKey(x => x.RegistrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Character>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Race).HasMaxLength(100);
            entity.Property(x => x.ClassOrType).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasOne(x => x.Person)
                .WithMany(x => x.Characters)
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Appearances)
                .WithOne(x => x.Character)
                .HasForeignKey(x => x.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CharacterAppearance>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.Character.IsDeleted);
            entity.Property(x => x.ContinuityStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne(x => x.Game)
                .WithMany()
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AssignedKingdom)
                .WithMany()
                .HasForeignKey(x => x.AssignedKingdomId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Registration)
                .WithMany()
                .HasForeignKey(x => x.RegistrationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MealOption>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
        });

        builder.Entity<FoodOrder>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.Registration.Person.IsDeleted && !x.Registration.Submission.IsDeleted);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.HasOne(x => x.MealOption)
                .WithMany()
                .HasForeignKey(x => x.MealOptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Payment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.Submission.IsDeleted);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Reference).HasMaxLength(100);
            entity.Property(x => x.Note).HasMaxLength(2000);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.RecordedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrganizerNote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Note).HasMaxLength(4000).IsRequired();
            entity.HasOne(x => x.Person)
                .WithMany(x => x.OrganizerNotes)
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EmailMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MailboxItemId).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.From).HasMaxLength(512).IsRequired();
            entity.Property(x => x.To).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(512).IsRequired();
            entity.Property(x => x.BodyText).HasMaxLength(20000);
            entity.Property(x => x.AttachmentMetadataJson).HasMaxLength(8000);
            entity.HasOne(x => x.LinkedSubmission)
                .WithMany()
                .HasForeignKey(x => x.LinkedSubmissionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.LinkedPerson)
                .WithMany()
                .HasForeignKey(x => x.LinkedPersonId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<GameRole>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.GameId, x.RoleName }).IsUnique();
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Game)
                .WithMany()
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.DetailsJson).HasMaxLength(8000);
        });

        builder.Entity<GameInvitation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RecipientEmail).HasMaxLength(256).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SentByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(4000);
            entity.HasOne(x => x.Game)
                .WithMany()
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.SentByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.GameId);
        });

        builder.Entity<HistoricalImportBatch>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Label).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SourceFormat).HasMaxLength(80).IsRequired();
            entity.Property(x => x.SourceFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ImportedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.NotesJson).HasMaxLength(4000);
            entity.HasOne(x => x.Game)
                .WithMany()
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.ImportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Rows)
                .WithOne(x => x.LastBatch)
                .HasForeignKey(x => x.LastBatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<LoginToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Token).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasIndex(x => new { x.Email, x.CreatedAtUtc });
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Announcement>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.HtmlContent).IsRequired();
            entity.HasMany(x => x.Dismissals)
                .WithOne(x => x.Announcement)
                .HasForeignKey(x => x.AnnouncementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AnnouncementDismissal>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AnnouncementId, x.UserId }).IsUnique();
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<HistoricalImportRow>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceFormat).HasMaxLength(80).IsRequired();
            entity.Property(x => x.SourceSheet).HasMaxLength(120).IsRequired();
            entity.Property(x => x.SourceKey).HasMaxLength(300).IsRequired();
            entity.Property(x => x.SourceLabel).HasMaxLength(300).IsRequired();
            entity.Property(x => x.WarningMessage).HasMaxLength(1000);
            entity.HasIndex(x => new { x.SourceFormat, x.SourceSheet, x.SourceKey }).IsUnique();
            entity.HasOne(x => x.LinkedPerson)
                .WithMany()
                .HasForeignKey(x => x.LinkedPersonId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.LinkedSubmission)
                .WithMany()
                .HasForeignKey(x => x.LinkedSubmissionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.LinkedRegistration)
                .WithMany()
                .HasForeignKey(x => x.LinkedRegistrationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.LinkedCharacter)
                .WithMany()
                .HasForeignKey(x => x.LinkedCharacterId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Room>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        builder.Entity<GameRoom>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.GameId, x.RoomId }).IsUnique();
        });

        builder.Entity<ExternalContact>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
        });

        builder.Entity<UserEmail>(entity =>
        {
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.HasOne(x => x.User)
                .WithMany(x => x.AlternateEmails)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
