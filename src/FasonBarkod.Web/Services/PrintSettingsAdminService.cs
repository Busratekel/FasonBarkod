using FasonBarkod.Infrastructure.Printing;
using FasonBarkod.Web.Models;
using FasonBarkod.Web.Services;

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
        var installedPrinters = printerDiscovery.GetInstalledPrinters().ToList();

        if (!string.IsNullOrWhiteSpace(settings.PrinterName)
            && !installedPrinters.Contains(settings.PrinterName, StringComparer.OrdinalIgnoreCase))
        {
            installedPrinters.Insert(0, settings.PrinterName);
        }

        return new PrintSettingsViewModel
        {
            InstalledPrinters = installedPrinters,
            DefaultPrinterName = defaultPrinter,
            SelectedPrinterName = settings.PrinterName ?? defaultPrinter ?? string.Empty,
            UseWindowsDefaultPrinter = string.IsNullOrWhiteSpace(settings.PrinterName)
        };
    }
}
