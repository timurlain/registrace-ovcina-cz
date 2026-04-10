using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using RegistraceOvcina.Web.Features.Announcements;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Data;

public static class DatabaseInitializer
{
    private static readonly (string Name, string Category)[] SeedRoles =
    [
        (RoleNames.Admin, "system"),
        (RoleNames.Organizer, "system"),
        (RoleNames.Registrant, "system"),
        (RoleNames.Guest, "system"),
        (RoleNames.King, "game"),
        (RoleNames.Merchant, "game"),
        (RoleNames.Player, "game"),
        (RoleNames.Healer, "game"),
        (RoleNames.StaffRegistration, "staff"),
        (RoleNames.StaffAccounts, "staff"),
        (RoleNames.StaffLogistics, "staff"),
    ];

    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

        var applyMigrations = configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup")
            ?? (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

        if (!applyMigrations)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (configuration.GetValue("SeedData:ResetDatabase", false))
        {
            await db.Database.EnsureDeletedAsync();
        }

        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
        {
            // Tables already exist but migration history is out of sync.
            // Drop and recreate to get a clean state (dev/early prod only).
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
            logger.LogWarning("Migration failed (tables exist). Resetting database to sync migration history.");
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        await SeedKingdomsAsync(db);
        await SeedAnnouncementsAsync(db);

        // Roles must always exist — external login creates users with Registrant role
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var (name, category) in SeedRoles)
        {
            if (!await roleManager.RoleExistsAsync(name))
            {
                var createRoleResult = await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = name,
                    Category = category,
                });
                EnsureSuccess(createRoleResult, $"role '{name}'");
            }
            else
            {
                var existingRole = await roleManager.FindByNameAsync(name);
                if (existingRole is not null && existingRole.Category != category)
                {
                    existingRole.Category = category;
                    await roleManager.UpdateAsync(existingRole);
                }
            }
        }

        // Production admin accounts — always seed, regardless of SeedDemoUsers
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var nowUtc = DateTime.UtcNow;
        var adminRoles = new[] { RoleNames.Registrant, RoleNames.Organizer, RoleNames.Admin };
        var adminFallbackPassword = $"OAuth!{Guid.NewGuid():N}";

        await EnsureUserAsync(userManager, "tomas.pajonk@hotmail.cz", adminFallbackPassword,
            "Tomáš Pajonk", nowUtc, adminRoles);
        await EnsureUserAsync(userManager, "stanam@email.cz", adminFallbackPassword,
            "Stanam", nowUtc, adminRoles);
        await EnsureUserAsync(userManager, "bl.richtar@gmail.com", adminFallbackPassword,
            "Blanka Richtar", nowUtc, adminRoles);

        var seedDemoUsers = configuration.GetValue<bool?>("SeedData:SeedDemoUsers")
            ?? (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

        if (!seedDemoUsers)
        {
            return;
        }

        await EnsureUserAsync(
            userManager,
            configuration["SeedData:AdminEmail"] ?? "admin@ovcina.test",
            configuration["SeedData:AdminPassword"] ?? "Pass123!",
            configuration["SeedData:AdminDisplayName"] ?? "Správce Ovčiny",
            nowUtc,
            [RoleNames.Registrant, RoleNames.Organizer, RoleNames.Admin]);

        await EnsureUserAsync(
            userManager,
            configuration["SeedData:RegistrantEmail"] ?? "registrant@ovcina.test",
            configuration["SeedData:RegistrantPassword"] ?? "Pass123!",
            configuration["SeedData:RegistrantDisplayName"] ?? "Ukázkový registrující",
            nowUtc,
            [RoleNames.Registrant]);

        await SeedGameDataAsync(db, nowUtc);
    }

    private static async Task SeedKingdomsAsync(ApplicationDbContext db)
    {
        var canonical = new[]
        {
            ("Aradhryand",       "Elfové",            "#2E7D32"),
            ("Azanulinbar-Dum",  "Trpaslíci",         "#C62828"),
            ("Esgaroth",         "Jezerní lidé",      "#1565C0"),
            ("Novy-Arnor",       "Nový Arnor",        "#F9A825"),
        };

        foreach (var (name, displayName, color) in canonical)
        {
            if (!await db.Kingdoms.AnyAsync(k => k.Name == name))
            {
                db.Kingdoms.Add(new Kingdom
                {
                    Name = name,
                    DisplayName = displayName,
                    Color = color
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedAnnouncementsAsync(ApplicationDbContext db)
    {
        if (await db.Announcements.AnyAsync())
        {
            return;
        }

        db.Announcements.Add(new Announcement
        {
            Title = "30. Ovčina — Balinova pozvánka",
            HtmlContent = """
                <div style="font-family: 'Segoe UI', Arial, sans-serif; max-width: 600px; margin: 0 auto; color: #2C1810; line-height: 1.6;">

                  <h2 style="color: #3C2415; border-bottom: 3px solid #B26223; padding-bottom: 8px;">
                    🐑 30. Ovčina — Balinova pozvánka
                  </h2>

                  <p>Milí kamarádi,</p>

                  <p>Spouštíme registraci na letošní Ovčinu! Ovčina proběhne <strong>první víkend v květnu</strong>.</p>

                  <p>
                    <a href="https://registrace.ovcina.cz" style="display: inline-block; background: #B22222; color: #fff; padding: 12px 24px; border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 16px;">
                      Registrovat se na registrace.ovcina.cz
                    </a>
                  </p>

                  <p>Dejte nám prosím <strong>co nejdříve</strong> vědět, zda dorazíte a zda si objednáváte jídlo či přespání. Ihned po registraci se vám vygeneruje QR kód, který prosím obratem uhraďte.</p>

                  <h3 style="color: #8B4513; margin-top: 24px;">Program</h3>
                  <p>Struktura hry zůstává podobná:</p>
                  <ul>
                    <li>Příjezd nejpozději v <strong>pátek 1. května v 8:00</strong></li>
                    <li>Pátek celý den a sobota do cca 18:00 — hra</li>
                    <li>Po hře hostina</li>
                    <li>Kdo chce pomoct s úklidem, může zůstat do neděle</li>
                  </ul>

                  <h3 style="color: #8B4513; margin-top: 24px;">Jídlo</h3>

                  <table style="border-collapse: collapse; width: 100%; margin-bottom: 16px;">
                    <tr style="background: #3C2415; color: #FFF8F0;">
                      <th style="padding: 8px; text-align: left;" colspan="2">Sobota</th>
                    </tr>
                    <tr style="background: #F5EDE3;">
                      <td style="padding: 6px 8px; font-weight: 600;">Svačina</td>
                      <td style="padding: 6px 8px;">Bageta se šunkou a sýrem</td>
                    </tr>
                    <tr>
                      <td style="padding: 6px 8px; font-weight: 600;">Oběd</td>
                      <td style="padding: 6px 8px;">Řízky, chleba, zelenina</td>
                    </tr>
                    <tr style="background: #F5EDE3;">
                      <td style="padding: 6px 8px; font-weight: 600;">Svačina</td>
                      <td style="padding: 6px 8px;">Tatranka nebo perník, jablko</td>
                    </tr>
                    <tr>
                      <td style="padding: 6px 8px; color: #8B4513;" colspan="2"><em>Večeři si řeší každý sám — bude se opékat, špekáčky můžete dát do lednice.</em></td>
                    </tr>
                  </table>

                  <table style="border-collapse: collapse; width: 100%; margin-bottom: 16px;">
                    <tr style="background: #3C2415; color: #FFF8F0;">
                      <th style="padding: 8px; text-align: left;" colspan="2">Neděle</th>
                    </tr>
                    <tr>
                      <td style="padding: 6px 8px; color: #8B4513;" colspan="2"><em>Snídani si řeší každý sám</em></td>
                    </tr>
                    <tr style="background: #F5EDE3;">
                      <td style="padding: 6px 8px; font-weight: 600;">Svačina</td>
                      <td style="padding: 6px 8px;">Tatranka nebo perník, jablko</td>
                    </tr>
                    <tr>
                      <td style="padding: 6px 8px; font-weight: 600;">Oběd</td>
                      <td style="padding: 6px 8px;">Vepřová kýta s chlebem</td>
                    </tr>
                  </table>

                  <div style="background: #FFF8F0; border: 1px solid #D4C4B0; border-left: 4px solid #B26223; padding: 12px 16px; border-radius: 6px; margin: 16px 0;">
                    <strong>🥤 Nezapomeňte!</strong> Letos dětem přibalte hrníčky / termosky na vodu a čaj — budou k dispozici v průběhu hry. Chceme minimalizovat jednorázové kelímky.
                  </div>

                  <h3 style="color: #8B4513; margin-top: 24px;">Ceny</h3>

                  <table style="border-collapse: collapse; width: 100%; margin-bottom: 16px;">
                    <tr style="background: #3C2415; color: #FFF8F0;">
                      <th style="padding: 8px; text-align: left;">Položka</th>
                      <th style="padding: 8px; text-align: right;">Cena</th>
                    </tr>
                    <tr style="background: #F5EDE3;">
                      <td style="padding: 6px 8px;">Hra — dítě</td>
                      <td style="padding: 6px 8px; text-align: right; font-weight: 600;">200 Kč</td>
                    </tr>
                    <tr>
                      <td style="padding: 6px 8px;">Hra — příšery (dospělí)</td>
                      <td style="padding: 6px 8px; text-align: right; font-weight: 600; color: #2E7D32;">zdarma</td>
                    </tr>
                    <tr style="background: #F5EDE3;">
                      <td style="padding: 6px 8px;">Jídlo — dítě (oba dny)</td>
                      <td style="padding: 6px 8px; text-align: right; font-weight: 600;">340 Kč <span style="color: #8B4513; font-weight: normal;">(170/den)</span></td>
                    </tr>
                    <tr>
                      <td style="padding: 6px 8px;">Jídlo — dospělý (oba dny)</td>
                      <td style="padding: 6px 8px; text-align: right; font-weight: 600;">460 Kč <span style="color: #8B4513; font-weight: normal;">(230/den)</span></td>
                    </tr>
                    <tr style="background: #F5EDE3;">
                      <td style="padding: 6px 8px;">Přespání — pod střechou</td>
                      <td style="padding: 6px 8px; text-align: right; font-weight: 600;">150 Kč <span style="color: #8B4513; font-weight: normal;">/os./noc</span></td>
                    </tr>
                    <tr>
                      <td style="padding: 6px 8px;">Přespání — stan / širák</td>
                      <td style="padding: 6px 8px; text-align: right; font-weight: 600;">50 Kč <span style="color: #8B4513; font-weight: normal;">/os. za oba dny</span></td>
                    </tr>
                  </table>

                  <p style="text-align: center; margin-top: 24px;">
                    <a href="https://registrace.ovcina.cz" style="display: inline-block; background: #B22222; color: #fff; padding: 14px 32px; border-radius: 6px; text-decoration: none; font-weight: 700; font-size: 18px;">
                      Zaregistrovat se
                    </a>
                  </p>

                  <h3 style="color: #8B4513; margin-top: 24px;">WhatsApp</h3>
                  <p>Pro aktuální informace se přidejte do <a href="https://chat.whatsapp.com/H6RAc1NbyBvBLC06ImAhpk" style="color: #B22222;">WhatsApp skupiny (komunity)</a>. Notifikace je možno ztišit a nakouknut jen když máte prostor.</p>

                  <hr style="border: none; border-top: 2px solid #D4C4B0; margin: 24px 0;" />

                  <p style="color: #8B4513; font-size: 14px;">
                    S pozdravem,<br />
                    <strong>Organizátoři Ovčiny</strong><br />
                    ovcina@ovcina.cz
                  </p>

                </div>
                """,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedGameDataAsync(ApplicationDbContext db, DateTime nowUtc)
    {
        if (await db.Games.AnyAsync())
        {
            return;
        }

        var game = new Game
        {
            Name = "30. Ovčina Balinova pozvánka",
            Description = "Co se skrývá v Morii?",
            StartsAtUtc = new DateTime(2026, 5, 1, 7, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 5, 2, 16, 0, 0, DateTimeKind.Utc),
            RegistrationClosesAtUtc = new DateTime(2026, 4, 28, 15, 0, 0, DateTimeKind.Utc),
            MealOrderingClosesAtUtc = new DateTime(2026, 4, 28, 15, 0, 0, DateTimeKind.Utc),
            PaymentDueAtUtc = new DateTime(2026, 4, 29, 15, 0, 0, DateTimeKind.Utc),
            AssignmentFreezeAtUtc = new DateTime(2026, 4, 30, 21, 30, 0, DateTimeKind.Utc),
            PlayerBasePrice = 100m,
            AdultHelperBasePrice = 0m,
            BankAccount = "CZ6508000000192000145399",
            BankAccountName = "Ovčina z.s.",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            TargetPlayerCountTotal = 120,
            IsPublished = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string displayName,
        DateTime nowUtc,
        IReadOnlyCollection<string> roles)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                IsActive = true,
                CreatedAtUtc = nowUtc
            };

            var createUserResult = await userManager.CreateAsync(user, password);
            EnsureSuccess(createUserResult, $"user '{email}'");
        }
        else
        {
            user.DisplayName = displayName;
            user.IsActive = true;
            user.EmailConfirmed = true;

            var updateResult = await userManager.UpdateAsync(user);
            EnsureSuccess(updateResult, $"user '{email}' update");
        }

        foreach (var role in roles)
        {
            if (!await userManager.IsInRoleAsync(user, role))
            {
                var addToRoleResult = await userManager.AddToRoleAsync(user, role);
                EnsureSuccess(addToRoleResult, $"assign role '{role}' to '{email}'");
            }
        }
    }

    public static async Task SeedOidcClientsAsync(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        await EnsureClientAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId = "baca",
            ClientSecret = "baca-dev-secret-change-in-production",
            DisplayName = "Bača — Task Tracker",
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            RedirectUris =
            {
                new Uri("https://baca.ovcina.cz/auth/callback"),
                new Uri("http://localhost:3000/auth/callback"),
            },
            PostLogoutRedirectUris =
            {
                new Uri("https://baca.ovcina.cz/"),
                new Uri("http://localhost:3000/"),
            },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}roles",
            },
        });

        await EnsureClientAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId = "ovcinahra",
            ClientSecret = "ovcinahra-dev-secret-change-in-production",
            DisplayName = "OvčinaHra — World App",
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            RedirectUris =
            {
                new Uri("https://hra.ovcina.cz/auth-callback"),
                new Uri("http://localhost:5290/auth-callback"),
            },
            PostLogoutRedirectUris =
            {
                new Uri("https://hra.ovcina.cz/"),
                new Uri("http://localhost:5290/"),
            },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}roles",
            },
        });
    }

    private static async Task EnsureClientAsync(
        IOpenIddictApplicationManager manager,
        OpenIddictApplicationDescriptor descriptor)
    {
        var existing = await manager.FindByClientIdAsync(descriptor.ClientId!);
        if (existing is null)
        {
            await manager.CreateAsync(descriptor);
        }
        else
        {
            await manager.UpdateAsync(existing, descriptor);
        }
    }

    private static void EnsureSuccess(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Failed to complete {operation}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }
}
