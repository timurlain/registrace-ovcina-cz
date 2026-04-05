using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Data;

public static class DatabaseInitializer
{
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

        await db.Database.MigrateAsync();

        await SeedKingdomsAsync(db);

        // Roles must always exist — external login creates users with Registrant role
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { RoleNames.Registrant, RoleNames.Organizer, RoleNames.Admin })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var createRoleResult = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSuccess(createRoleResult, $"role '{role}'");
            }
        }

        var seedDemoUsers = configuration.GetValue<bool?>("SeedData:SeedDemoUsers")
            ?? (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

        if (!seedDemoUsers)
        {
            return;
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var nowUtc = DateTime.UtcNow;

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

        // Production admin accounts (OAuth login, password is a fallback only)
        var adminRoles = new[] { RoleNames.Registrant, RoleNames.Organizer, RoleNames.Admin };
        var adminFallbackPassword = "OvcinaAdmin2026!Xk9$";

        await EnsureUserAsync(userManager, "tomas.pajonk@hotmail.cz", adminFallbackPassword,
            "Tomáš Pajonk", nowUtc, adminRoles);
        await EnsureUserAsync(userManager, "stanam@email.cz", adminFallbackPassword,
            "Stanam", nowUtc, adminRoles);
        await EnsureUserAsync(userManager, "blanka.richtar@gmail.com", adminFallbackPassword,
            "Blanka Richtar", nowUtc, adminRoles);

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
