using FasonBarkod.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FasonBarkod.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DbInitializer");

        try
        {
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Bekleyen migration'lar: {Migrations}", string.Join(", ", pending));
            }

            await context.Database.MigrateAsync();
            logger.LogInformation("Veritabanı migration tamamlandı.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(
                ex,
                "Veritabanı migration başarısız. SQL bağlantısını, web.admin yetkilerini ve " +
                "__EFMigrationsHistory tablosunu kontrol edin.");
            throw;
        }

        // İlk kurulum: yalnızca hiç kullanıcı yoksa ve Bootstrap ayarları doluysa admin oluşturur.
        await TryBootstrapAdminAsync(scope.ServiceProvider, logger);
    }

    private static async Task TryBootstrapAdminAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetService<UserManager<ApplicationUser>>();
        var roleManager = services.GetService<RoleManager<IdentityRole>>();
        var configuration = services.GetService<IConfiguration>();

        if (userManager is null || roleManager is null || configuration is null)
        {
            return;
        }

        if (await userManager.Users.AnyAsync())
        {
            return;
        }

        var email = configuration["Bootstrap:AdminEmail"]?.Trim();
        var password = configuration["Bootstrap:AdminPassword"];
        var fullName = configuration["Bootstrap:AdminFullName"]?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Veritabanında kullanıcı yok. İlk admin için Bootstrap:AdminEmail ve Bootstrap:AdminPassword ayarlayın, " +
                "uygulamayı bir kez başlatın, ardından Bootstrap şifresini kaldırın.");
            return;
        }

        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }

        if (!await roleManager.RoleExistsAsync("Operator"))
        {
            await roleManager.CreateAsync(new IdentityRole("Operator"));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = string.IsNullOrWhiteSpace(fullName) ? "Sistem Yöneticisi" : fullName,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "İlk admin oluşturulamadı: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, "Admin");
        logger.LogInformation("İlk admin oluşturuldu: {Email}. Bootstrap şifresini yapılandırmadan kaldırın.", email);
    }
}
