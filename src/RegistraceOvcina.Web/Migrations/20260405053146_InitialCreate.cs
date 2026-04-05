using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetailsJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistrationClosesAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MealOrderingClosesAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentDueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignmentFreezeAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlayerBasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AdultHelperBasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankAccount = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BankAccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VariableSymbolStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetPlayerCountTotal = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Kingdoms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kingdoms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BirthYear = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SentByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameInvitations_AspNetUsers_SentByUserId",
                        column: x => x.SentByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameInvitations_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceFormat = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    ImportedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalSourceRows = table.Column<int>(type: "integer", nullable: false),
                    HouseholdCount = table.Column<int>(type: "integer", nullable: false),
                    RegistrationCount = table.Column<int>(type: "integer", nullable: false),
                    PersonCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    PersonMatchedCount = table.Column<int>(type: "integer", nullable: false),
                    CharacterCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    NotesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportBatches_AspNetUsers_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoricalImportBatches_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealOptions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    RegistrantUserId = table.Column<string>(type: "text", nullable: false),
                    PrimaryContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PrimaryEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PrimaryPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastEditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpectedTotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RegistrantNote = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentVariableSymbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrationSubmissions_AspNetUsers_RegistrantUserId",
                        column: x => x.RegistrantUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegistrationSubmissions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GameKingdomTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    KingdomId = table.Column<int>(type: "integer", nullable: false),
                    TargetPlayerCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameKingdomTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameKingdomTargets_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameKingdomTargets_Kingdoms_KingdomId",
                        column: x => x.KingdomId,
                        principalTable: "Kingdoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Race = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClassOrType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MailboxItemId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    From = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    To = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BodyText = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LinkedSubmissionId = table.Column<int>(type: "integer", nullable: true),
                    LinkedPersonId = table.Column<int>(type: "integer", nullable: true),
                    AttachmentMetadataJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailMessages_People_LinkedPersonId",
                        column: x => x.LinkedPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailMessages_RegistrationSubmissions_LinkedSubmissionId",
                        column: x => x.LinkedSubmissionId,
                        principalTable: "RegistrationSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrganizerNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionId = table.Column<int>(type: "integer", nullable: true),
                    PersonId = table.Column<int>(type: "integer", nullable: true),
                    AuthorUserId = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizerNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizerNotes_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizerNotes_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizerNotes_RegistrationSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "RegistrationSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_RegistrationSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "RegistrationSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Registrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionId = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    AttendeeType = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PlayerSubType = table.Column<int>(type: "integer", nullable: true),
                    AdultRoles = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CharacterName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LodgingPreference = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreferredKingdomId = table.Column<int>(type: "integer", nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GuardianName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GuardianRelationship = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GuardianAuthorizationConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrantNote = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Registrations_Kingdoms_PreferredKingdomId",
                        column: x => x.PreferredKingdomId,
                        principalTable: "Kingdoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Registrations_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Registrations_RegistrationSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "RegistrationSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoodOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegistrationId = table.Column<int>(type: "integer", nullable: false),
                    MealOptionId = table.Column<int>(type: "integer", nullable: false),
                    MealDayUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodOrders_MealOptions_MealOptionId",
                        column: x => x.MealOptionId,
                        principalTable: "MealOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FoodOrders_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastBatchId = table.Column<int>(type: "integer", nullable: true),
                    SourceFormat = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceSheet = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceLabel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LinkedPersonId = table.Column<int>(type: "integer", nullable: true),
                    LinkedSubmissionId = table.Column<int>(type: "integer", nullable: true),
                    LinkedRegistrationId = table.Column<int>(type: "integer", nullable: true),
                    LinkedCharacterId = table.Column<int>(type: "integer", nullable: true),
                    WarningMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FirstImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_Characters_LinkedCharacterId",
                        column: x => x.LinkedCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_HistoricalImportBatches_LastBatchId",
                        column: x => x.LastBatchId,
                        principalTable: "HistoricalImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_People_LinkedPersonId",
                        column: x => x.LinkedPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_RegistrationSubmissions_LinkedSubmissi~",
                        column: x => x.LinkedSubmissionId,
                        principalTable: "RegistrationSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_Registrations_LinkedRegistrationId",
                        column: x => x.LinkedRegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CharacterAppearances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    RegistrationId = table.Column<int>(type: "integer", nullable: true),
                    LevelReached = table.Column<int>(type: "integer", nullable: true),
                    AssignedKingdomId = table.Column<int>(type: "integer", nullable: true),
                    ContinuityStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterAppearances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterAppearances_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterAppearances_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CharacterAppearances_Kingdoms_AssignedKingdomId",
                        column: x => x.AssignedKingdomId,
                        principalTable: "Kingdoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CharacterAppearances_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_LinkedPersonId",
                table: "EmailMessages",
                column: "LinkedPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_LinkedSubmissionId",
                table: "EmailMessages",
                column: "LinkedSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodOrders_MealOptionId",
                table: "FoodOrders",
                column: "MealOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodOrders_RegistrationId",
                table: "FoodOrders",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_GameInvitations_GameId",
                table: "GameInvitations",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameInvitations_SentByUserId",
                table: "GameInvitations",
                column: "SentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameKingdomTargets_GameId_KingdomId",
                table: "GameKingdomTargets",
                columns: new[] { "GameId", "KingdomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameKingdomTargets_KingdomId",
                table: "GameKingdomTargets",
                column: "KingdomId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportBatches_GameId",
                table: "HistoricalImportBatches",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportBatches_ImportedByUserId",
                table: "HistoricalImportBatches",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LastBatchId",
                table: "HistoricalImportRows",
                column: "LastBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedCharacterId",
                table: "HistoricalImportRows",
                column: "LinkedCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedPersonId",
                table: "HistoricalImportRows",
                column: "LinkedPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedRegistrationId",
                table: "HistoricalImportRows",
                column: "LinkedRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedSubmissionId",
                table: "HistoricalImportRows",
                column: "LinkedSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_SourceFormat_SourceSheet_SourceKey",
                table: "HistoricalImportRows",
                columns: new[] { "SourceFormat", "SourceSheet", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAppearances_AssignedKingdomId",
                table: "CharacterAppearances",
                column: "AssignedKingdomId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAppearances_GameId",
                table: "CharacterAppearances",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAppearances_CharacterId",
                table: "CharacterAppearances",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAppearances_RegistrationId",
                table: "CharacterAppearances",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_PersonId",
                table: "Characters",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Kingdoms_Name",
                table: "Kingdoms",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealOptions_GameId",
                table: "MealOptions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizerNotes_AuthorUserId",
                table: "OrganizerNotes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizerNotes_PersonId",
                table: "OrganizerNotes",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizerNotes_SubmissionId",
                table: "OrganizerNotes",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RecordedByUserId",
                table: "Payments",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SubmissionId",
                table: "Payments",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_People_LastName_FirstName_BirthYear",
                table: "People",
                columns: new[] { "LastName", "FirstName", "BirthYear" });

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_PersonId",
                table: "Registrations",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_PreferredKingdomId",
                table: "Registrations",
                column: "PreferredKingdomId");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_SubmissionId",
                table: "Registrations",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationSubmissions_GameId_RegistrantUserId",
                table: "RegistrationSubmissions",
                columns: new[] { "GameId", "RegistrantUserId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationSubmissions_RegistrantUserId",
                table: "RegistrationSubmissions",
                column: "RegistrantUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "EmailMessages");

            migrationBuilder.DropTable(
                name: "FoodOrders");

            migrationBuilder.DropTable(
                name: "GameInvitations");

            migrationBuilder.DropTable(
                name: "GameKingdomTargets");

            migrationBuilder.DropTable(
                name: "HistoricalImportRows");

            migrationBuilder.DropTable(
                name: "CharacterAppearances");

            migrationBuilder.DropTable(
                name: "OrganizerNotes");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "MealOptions");

            migrationBuilder.DropTable(
                name: "HistoricalImportBatches");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Registrations");

            migrationBuilder.DropTable(
                name: "Kingdoms");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "RegistrationSubmissions");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
