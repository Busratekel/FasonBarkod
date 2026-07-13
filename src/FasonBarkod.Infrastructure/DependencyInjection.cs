using System.Runtime.InteropServices;
using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Infrastructure.Printing;
using FasonBarkod.Infrastructure.Sap;
using FasonBarkod.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;

namespace FasonBarkod.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));

        services.Configure<SapOptions>(options => { });
        services.Configure<LdapOptions>(options => { });
        services.Configure<PrinterOptions>(options => { });

        services.AddMemoryCache();
        services.AddScoped<ISapConnectionFactory, SapConnectionFactory>();
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

        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));

        services.Configure<SapOptions>(configuration.GetSection(SapOptions.SectionName));
        services.Configure<LdapOptions>(configuration.GetSection(LdapOptions.SectionName));
        services.Configure<PrinterOptions>(configuration.GetSection(PrinterOptions.SectionName));

        services.AddMemoryCache();
        services.AddScoped<ISapConnectionFactory, SapConnectionFactory>();
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

        return services;
    }
}
