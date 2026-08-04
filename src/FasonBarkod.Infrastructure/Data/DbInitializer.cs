using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FasonBarkod.Infrastructure.Data;

public static class DbInitializer
{
    public const string DefaultCompanyCode = "DEFAULT";

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

        await EnsureRolesAndCompaniesAsync(scope.ServiceProvider, logger);
        await TryBootstrapAdminAsync(scope.ServiceProvider, logger);
    }

    private static async Task EnsureRolesAndCompaniesAsync(IServiceProvider services, ILogger logger)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetService<UserManager<ApplicationUser>>();
        var roleManager = services.GetService<RoleManager<IdentityRole>>();
        if (userManager is null || roleManager is null)
        {
            return;
        }

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Rol oluşturuldu: {Role}", role);
            }
        }

        var defaultCompany = await context.Companies
            .FirstOrDefaultAsync(c => c.Code == DefaultCompanyCode);
        if (defaultCompany is null)
        {
            defaultCompany = new Company
            {
                Name = "Varsayılan Şirket",
                Code = DefaultCompanyCode,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Companies.Add(defaultCompany);
            await context.SaveChangesAsync();
            logger.LogInformation("Varsayılan şirket oluşturuldu (Code={Code}).", DefaultCompanyCode);
        }

        var unassigned = await userManager.Users
            .Where(u => u.CompanyId == null)
            .ToListAsync();
        if (unassigned.Count > 0)
        {
            foreach (var user in unassigned)
            {
                var roles = await userManager.GetRolesAsync(user);
                // SuperAdmin şirket dışı kalabilir; diğerleri varsayılana bağlanır.
                if (roles.Contains(AppRoles.SuperAdmin) && !roles.Contains(AppRoles.Admin) && !roles.Contains(AppRoles.Operator))
                {
                    continue;
                }

                user.CompanyId = defaultCompany.Id;
                await userManager.UpdateAsync(user);
            }

            logger.LogInformation("{Count} kullanıcı varsayılan şirkete bağlandı.", unassigned.Count);
        }

        var superAdmins = await userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
        if (superAdmins.Count == 0)
        {
            var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
            var promote = admins.FirstOrDefault(a => a.IsActive) ?? admins.FirstOrDefault();
            if (promote is not null)
            {
                await userManager.AddToRoleAsync(promote, AppRoles.SuperAdmin);
                logger.LogInformation(
                    "Sistemde SuperAdmin yoktu; mevcut Admin yükseltildi: {Email}",
                    promote.Email);
            }
        }
    }

    private static async Task TryBootstrapAdminAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetService<UserManager<ApplicationUser>>();
        var roleManager = services.GetService<RoleManager<IdentityRole>>();
        var configuration = services.GetService<IConfiguration>();
        var context = services.GetService<ApplicationDbContext>();

        if (userManager is null || roleManager is null || configuration is null || context is null)
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

        var defaultCompany = await context.Companies
            .FirstAsync(c => c.Code == DefaultCompanyCode);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = string.IsNullOrWhiteSpace(fullName) ? "Sistem Yöneticisi" : fullName,
            IsActive = true,
            CompanyId = defaultCompany.Id
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "İlk admin oluşturulamadı: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, AppRoles.Admin);
        await userManager.AddToRoleAsync(user, AppRoles.SuperAdmin);
        logger.LogInformation(
            "İlk SuperAdmin oluşturuldu: {Email}. Bootstrap şifresini yapılandırmadan kaldırın.",
            email);
    }
}
