using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Infrastructure.Printing;
using FasonBarkod.Web.Models;
using FasonBarkod.Web.Services;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Web.Services;

public interface IPrintSettingsAdminService
{
    Task<PrintSettingsViewModel> BuildViewModelAsync(CancellationToken cancellationToken = default);
}

public class PrintSettingsAdminService(
    IPrinterDiscoveryService printerDiscovery,
    IPrintSettingsStore settingsStore) : IPrintSettingsAdminService
{
    public async Task<PrintSettingsViewModel> BuildViewModelAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var defaultPrinter = printerDiscovery.GetDefaultPrinterName();
        var installedPrinters = printerDiscovery.GetInstalledPrinters();

        return new PrintSettingsViewModel
        {
            InstalledPrinters = installedPrinters.ToList(),
            DefaultPrinterName = defaultPrinter,
            SelectedPrinterName = settings.PrinterName ?? defaultPrinter ?? string.Empty,
            UseWindowsDefaultPrinter = string.IsNullOrWhiteSpace(settings.PrinterName)
        };
    }
}
