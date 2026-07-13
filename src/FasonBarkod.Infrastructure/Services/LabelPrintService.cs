using System.Globalization;
using System.Runtime.InteropServices;
using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Infrastructure.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Infrastructure.Services;

public class LabelPrintService(
    IOptions<PrinterOptions> printerOptions,
    IPrinterDiscoveryService printerDiscovery,
    ILabelTemplateService labelTemplateService,
    ILogger<LabelPrintService> logger) : ILabelPrintService
{
    private static readonly Dictionary<string, string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@MAKTX"] = "MaterialDescription",
        ["@MATNR"] = "MaterialNumber",
        ["@ZPAKET"] = "PackageQuantity",
        ["@ZYIL"] = "Year",
        ["@ZHAFTA"] = "Week",
        ["@BARKOD1"] = "Barcode1",
        ["@BARKOD2"] = "Barcode2",
        ["@BARKOD3"] = "Barcode3",
        ["@BARKOD4"] = "Barcode4",
        ["@BRAND"] = "Brand",
        ["@SERNR"] = "SerialNumber",
        ["@EBELN"] = "PurchaseOrderNo",
        ["@EBELP"] = "LineNo",
        ["@BEZEI"] = "BrandText",
        ["@KUNNR"] = "VendorCode",
        ["@LIFNR"] = "VendorCode",
        ["@BARKOD3T"] = "Barcode3",
        ["@BARKOD4T"] = "Barcode4",
        ["@QR"] = "QrCode",
        ["@Counter"] = "Counter",
        ["@T"] = "Time",
        ["@ZBRKD_YIL"] = "Year",
        ["@ZBRKD_SAAT"] = "Time"
    };

    private readonly PrinterOptions _options = printerOptions.Value;

    public async Task<LabelPrintResult> PrintSasLabelsAsync(
        SasLabelPrintContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new LabelPrintResult(true, 0, "Yazıcı devre dışı (Printer:Enabled=false).");
        }

        var labelItems = BuildLabelItems(context);
        if (labelItems.Count == 0)
        {
            return new LabelPrintResult(false, 0, "Yazdırılacak etiket verisi yok.");
        }

        string template;
        try
        {
            template = await labelTemplateService.ResolveTemplateContentAsync(
                context.LabelType,
                context.MaterialGroupCode ?? labelItems[0].GetValueOrDefault("Brand"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Etiket şablonu çözümlenemedi");
            return new LabelPrintResult(false, 0, ex.Message);
        }

        var printed = 0;
        string? simulateFolder = null;

        foreach (var (values, index) in labelItems.Select((item, i) => (item, i)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var renderValues = EnrichRenderValues(values, index + 1, labelItems.Count);
            var rendered = RenderTemplate(template, renderValues);

            if (_options.SimulatePrint)
            {
                simulateFolder ??= await WriteSimulatedOutputAsync(rendered, context, printed, cancellationToken);
            }
            else
            {
                try
                {
                    var printerName = ResolvePrinterName(context.PrinterName);
                    logger.LogInformation("Etiket yazıcıya gönderiliyor: {Printer}", printerName);
                    SendToPhysicalPrinter(rendered, printerName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Fiziksel yazdırma başarısız");
                    return new LabelPrintResult(false, printed, ex.Message, simulateFolder);
                }
            }

            printed++;
        }

        logger.LogInformation(
            "{Count} etiket {Mode} — {LabelType} SAS {OrderNo}",
            printed,
            _options.SimulatePrint ? "simüle edildi" : "yazdırıldı",
            context.LabelType,
            context.PurchaseOrderNo);

        return new LabelPrintResult(
            true,
            printed,
            _options.SimulatePrint
                ? $"Simülasyon: {printed} etiket dosyaya yazıldı."
                : null,
            simulateFolder);
    }

    private string ResolvePrinterName(string? sessionPrinterName)
    {
        if (!string.IsNullOrWhiteSpace(sessionPrinterName))
        {
            return printerDiscovery.ResolvePrinterName(sessionPrinterName);
        }

        if (!string.IsNullOrWhiteSpace(_options.Name))
        {
            return _options.Name.Trim();
        }

        return printerDiscovery.ResolvePrinterName(null);
    }

    private static void SendToPhysicalPrinter(string rendered, string printerName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Fiziksel yazıcı yalnızca Windows'ta desteklenir.");
        }

        var bytes = WindowsRawPrinter.ToPrinterEncoding(rendered);
        WindowsRawPrinter.Send(printerName, bytes);
    }

    private async Task<string> WriteSimulatedOutputAsync(
        string rendered,
        SasLabelPrintContext context,
        int index,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(
            ResolveTemplateRoot(),
            _options.SimulateOutputFolder);

        Directory.CreateDirectory(folder);

        var fileName =
            $"{context.PurchaseOrderNo}_{context.LineNo}_{context.LabelType}_{DateTime.Now:yyyyMMdd_HHmmss}_{index + 1}.prn";
        var path = Path.Combine(folder, fileName);
        await File.WriteAllTextAsync(path, rendered, PrinterEncoding.Turkish, cancellationToken);
        return folder;
    }

    private string ResolveTemplateRoot()
    {
        if (Path.IsPathRooted(_options.TemplateFolder))
        {
            return _options.TemplateFolder;
        }

        return Path.Combine(AppContext.BaseDirectory, _options.TemplateFolder);
    }

    private static Dictionary<string, string> EnrichRenderValues(
        Dictionary<string, string> values,
        int counter,
        int totalCount)
    {
        var now = DateTime.Now;
        var enriched = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
        {
            ["Counter"] = counter.ToString(CultureInfo.InvariantCulture),
            ["Time"] = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["QrCode"] = values.GetValueOrDefault("Barcode1") ?? string.Empty,
            ["VendorCode"] = values.GetValueOrDefault("VendorCode") ?? string.Empty
        };

        if (!enriched.ContainsKey("Year"))
        {
            enriched["Year"] = now.Year.ToString(CultureInfo.InvariantCulture);
        }

        return enriched;
    }

    private static List<Dictionary<string, string>> BuildLabelItems(SasLabelPrintContext context)
    {
        var now = DateTime.Now;
        var calendar = CultureInfo.CurrentCulture.Calendar;
        var week = calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

        if (context.SapResult.Labels is { Count: > 0 } sapLabels)
        {
            return sapLabels.Select(label => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MaterialDescription"] = label.MaterialDescription,
                ["MaterialNumber"] = label.MaterialNumber,
                ["PackageQuantity"] = FormatQuantity(label.PackageQuantity),
                ["Year"] = now.Year.ToString(CultureInfo.InvariantCulture),
                ["Week"] = week.ToString(CultureInfo.InvariantCulture),
                ["Barcode1"] = label.Barcode1,
                ["Barcode2"] = label.Barcode2,
                ["Barcode3"] = label.Barcode3,
                ["Barcode4"] = label.Barcode4,
                ["Brand"] = label.BrandCode,
                ["BrandText"] = label.BrandText,
                ["SerialNumber"] = label.SerialNumber,
                ["PurchaseOrderNo"] = context.PurchaseOrderNo,
                ["LineNo"] = context.LineNo,
                ["VendorCode"] = context.VendorCode ?? string.Empty
            }).ToList();
        }

        return context.SapResult.BarcodeNumbers.Select(barcode => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MaterialDescription"] = context.MaterialDescription,
            ["MaterialNumber"] = context.MaterialNumber,
            ["PackageQuantity"] = FormatQuantity(context.PackageQuantity),
            ["Year"] = now.Year.ToString(CultureInfo.InvariantCulture),
            ["Week"] = week.ToString(CultureInfo.InvariantCulture),
            ["Barcode1"] = barcode,
            ["Barcode2"] = string.Empty,
            ["Barcode3"] = string.Empty,
            ["Barcode4"] = string.Empty,
            ["Brand"] = context.MaterialGroupCode ?? string.Empty,
            ["BrandText"] = string.Empty,
            ["SerialNumber"] = context.SerialNumber ?? string.Empty,
            ["PurchaseOrderNo"] = context.PurchaseOrderNo,
            ["LineNo"] = context.LineNo,
            ["VendorCode"] = context.VendorCode ?? string.Empty
        }).ToList();
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;

        foreach (var (placeholder, key) in Placeholders)
        {
            values.TryGetValue(key, out var value);
            result = result.Replace(placeholder, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string FormatQuantity(decimal quantity) =>
        quantity % 1 == 0
            ? ((int)quantity).ToString(CultureInfo.InvariantCulture)
            : quantity.ToString("0.##", CultureInfo.InvariantCulture);
}
