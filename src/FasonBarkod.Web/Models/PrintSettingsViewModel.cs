namespace FasonBarkod.Web.Models;

public class PrintSettingsViewModel
{
    public bool UseWindowsDefaultPrinter { get; set; } = true;

    public string? SelectedPrinterName { get; set; }

    public string? DefaultPrinterName { get; set; }

    public List<string> InstalledPrinters { get; set; } = [];

    /// <summary>true: yazıcı listesi QZ Tray ile istemciden alınır.</summary>
    public bool UseQzTray { get; set; }
}

public class SavePrintSettingsViewModel
{
    public bool UseWindowsDefaultPrinter { get; set; }

    public string? SelectedPrinterName { get; set; }
}
