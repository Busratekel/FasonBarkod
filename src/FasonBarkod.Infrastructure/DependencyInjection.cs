using System.Runtime.InteropServices;
using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Infrastructure.Printing;
using FasonBarkod.Infrastructure.Sap;
using FasonBarkod.Infrastructure.Sap.Soap;
using FasonBarkod.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FasonBarkod.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options => ConfigureDbContext(options, connectionString));

        services.Configure<SapOptions>(options => { });
        services.Configure<SapBarcodeSoapOptions>(options => { });
        services.Configure<LdapOptions>(options => { });
        services.Configure<PrinterOptions>(options => { });

        RegisterSharedServices(services);
        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options => ConfigureDbContext(options, connectionString));

        services.Configure<SapOptions>(configuration.GetSection(SapOptions.SectionName));
        services.Configure<SapBarcodeSoapOptions>(configuration.GetSection(SapBarcodeSoapOptions.SectionName));
        services.Configure<LdapOptions>(configuration.GetSection(LdapOptions.SectionName));
        services.Configure<PrinterOptions>(configuration.GetSection(PrinterOptions.SectionName));

        RegisterSharedServices(services);
        return services;
    }

    private static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseSqlServer(connectionString)
            // EF 10: MigrateAsync model/snapshot mikro farklarında uygulamayı düşürmesin.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    private static void RegisterSharedServices(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<ISapConnectionFactory, SapConnectionFactory>();
        services.AddScoped<ISapBarcodeSoapClient, SapBarcodeSoapClient>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBarcodeService, BarcodeService>();
        services.AddScoped<ISapService, SapService>();
        services.AddScoped<WindowsPrinterDiscoveryService>();
        services.AddScoped<IPrinterDiscoveryService>(sp =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? sp.GetRequiredService<CachedPrinterDiscoveryService>()
                : new UnsupportedPrinterDiscoveryService());
        services.AddScoped<CachedPrinterDiscoveryService>();
        services.AddScoped<ILabelPrintService, LabelPrintService>();
        services.AddScoped<ILabelTemplateService, LabelTemplateService>();
        services.AddScoped<BarcodeNumberService>();
    }
}
