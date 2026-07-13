using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FasonBarkod.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        // Çöken süreçler SQLite migration kilidini bırakmayabilir (geliştirme ortamı).
        if (context.Database.IsSqlite())
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsLock\" WHERE \"Id\" = 1;");
            }
            catch
            {
                // İlk çalıştırmada kilit tablosu henüz olmayabilir.
            }
        }

        await context.Database.MigrateAsync();

        await CleanupNonSapPrintRecordsAsync(context, logger);

        // API projesinde Identity yok; kullanıcı seed sadece Web'de çalışır.
        var userManager = scope.ServiceProvider.GetService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetService<RoleManager<IdentityRole>>();

        if (userManager is not null && roleManager is not null)
        {
            await EnsureRoleAsync(roleManager, "Admin");
            await EnsureRoleAsync(roleManager, "Operator");

            await EnsureUserAsync(userManager, "admin@fason.local", "Admin123!", "Sistem Yöneticisi", "Admin");
            await EnsureUserAsync(userManager, "operator@fason.local", "Operator123!", "Operatör Kullanıcı", "Operator");
        }

        if (!await context.AppPrintSettings.AnyAsync())
        {
            context.AppPrintSettings.Add(new AppPrintSettings { Id = AppPrintSettings.SingletonId });
            await context.SaveChangesAsync();
        }

        if (!await context.SalesOrderLines.AnyAsync())
        {
            context.SalesOrderLines.AddRange(
                new SalesOrderLine
                {
                    SalesOrderNo = "50001234",
                    CustomerCode = "C001",
                    CustomerName = "Bellona",
                    MaterialCode = "YTK-001",
                    MaterialName = "Karyola",
                    Color = "Antrasit",
                    Quantity = 100,
                    OrderDate = DateTime.UtcNow.Date.AddDays(-2)
                },
                new SalesOrderLine
                {
                    SalesOrderNo = "50001234",
                    CustomerCode = "C001",
                    CustomerName = "Bellona",
                    MaterialCode = "YTK-002",
                    MaterialName = "Komodin",
                    Color = "Beyaz",
                    Quantity = 50,
                    OrderDate = DateTime.UtcNow.Date.AddDays(-2)
                },
                new SalesOrderLine
                {
                    SalesOrderNo = "50005678",
                    CustomerCode = "C002",
                    CustomerName = "Doğtaş",
                    MaterialCode = "KLT-010",
                    MaterialName = "Gardrop",
                    Color = "Ceviz",
                    Quantity = 25,
                    OrderDate = DateTime.UtcNow.Date.AddDays(-1)
                });

            await context.SaveChangesAsync();
            logger.LogInformation("Örnek sipariş kalemleri eklendi.");
        }
    }

    private static async Task CleanupNonSapPrintRecordsAsync(
        ApplicationDbContext context,
        ILogger logger)
    {
        var testPrints = await context.BarcodePrints
            .Where(x =>
                x.BarcodeNo.StartsWith("MOCK-") ||
                x.BarcodeNo.StartsWith("FSN-"))
            .ToListAsync();

        if (testPrints.Count == 0)
        {
            return;
        }

        var ids = testPrints.Select(x => x.Id).ToList();
        var logs = await context.SapTransferLogs
            .Where(x => ids.Contains(x.BarcodePrintId))
            .ToListAsync();

        context.SapTransferLogs.RemoveRange(logs);
        context.BarcodePrints.RemoveRange(testPrints);
        await context.SaveChangesAsync();
        logger.LogInformation("Test / mock basım kayıtları temizlendi: {Count}", testPrints.Count);
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string fullName,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            return;
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }
}
