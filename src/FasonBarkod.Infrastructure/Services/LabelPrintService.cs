using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
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
        ["@C"] = "Counter",
        ["@T"] = "Time",
        ["@TARIH"] = "Date",
        ["@ZBRKD_YIL"] = "Year",
        ["@ZBRKD_SAAT"] = "Time"
    };

    private readonly PrinterOptions _options = printerOptions.Value;

    public LabelPrintResult SendRawTestPrint(string? printerName = null)
    {
        if (!_options.Enabled)
        {
            return new LabelPrintResult(false, 0, "Yazıcı devre dışı (Printer:Enabled=false).");
        }

        if (_options.SimulatePrint)
        {
            return new LabelPrintResult(false, 0, "SimulatePrint=true. Fiziksel test için false yapın.");
        }

        // Minimal SBPL — bağlantı + medya boyutu (A1) testi.
        const char esc = '\u001B';
        var sbpl = EnsureMediaSize(
            $"{esc}A\n" +
            $"{esc}V0100{esc}H0100{esc}XMTEST SATO OK\n" +
            $"{esc}V0180{esc}H0100{esc}XS{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"{esc}V0260{esc}H0100{esc}BG02100TEST123456\n" +
            $"{esc}V0360{esc}H0100{esc}XSTEST123456\n" +
            $"{esc}Q000001\n" +
            $"{esc}Z");

        try
        {
            var resolved = ResolvePrinterName(printerName);
            logger.LogDebug("Test etiket: {Printer}", resolved);
            SendToPhysicalPrinter(sbpl, resolved);
            return new LabelPrintResult(true, 1, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test baskı başarısız");
            return new LabelPrintResult(false, 0, ex.Message);
        }
    }

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
        var renderedLabels = new List<string>(labelItems.Count);

        foreach (var (values, index) in labelItems.Select((item, i) => (item, i)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var renderValues = EnrichRenderValues(values, index + 1, labelItems.Count);
            var rendered = EnsureMediaSize(RenderTemplate(template, renderValues));
            renderedLabels.Add(rendered);

            if (_options.SimulatePrint)
            {
                // Her etiket için ayrı .prn yazılmalı. ??= yalnızca ilk çağrıyı çalıştırıyordu;
                // bu yüzden 3/5 etiket basılsa bile klasörde tek dosya kalıyordu.
                var folder = await WriteSimulatedOutputAsync(rendered, context, printed, cancellationToken);
                simulateFolder ??= folder;
            }

            printed++;
        }

        // QZ Tray: etiket içeriğini istemciye ver; sunucu spooler kullanmaz.
        if (_options.UseQzTray && !_options.SimulatePrint)
        {
            var asciiJobs = renderedLabels
                .Select(PrinterEncoding.ToAsciiSafe)
                .ToList();

            logger.LogDebug(
                "{Count} etiket QZ için hazır — {LabelType} {OrderNo}",
                printed,
                context.LabelType,
                context.PurchaseOrderNo);

            return new LabelPrintResult(
                true,
                printed,
                null,
                simulateFolder,
                asciiJobs,
                UseQzTray: true);
        }

        if (!_options.SimulatePrint)
        {
            try
            {
                var printerName = ResolvePrinterName(context.PrinterName);
                logger.LogDebug(
                    "{Count} etiket yazıcıya: {Printer}",
                    renderedLabels.Count,
                    printerName);
                // Tek StartDoc — her etiket için ayrı job açmak dolu/boş beslemeye yol açıyordu.
                var bytes = WindowsRawPrinter.ConcatRawJobs(renderedLabels);
                WindowsRawPrinter.Send(printerName, bytes);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fiziksel yazdırma başarısız");
                return new LabelPrintResult(false, 0, ex.Message, simulateFolder);
            }
        }

        logger.LogDebug(
            "{Count} etiket {Mode} — {LabelType} {OrderNo}",
            printed,
            _options.SimulatePrint ? "simüle" : "yazdırıldı",
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

    /// <summary>
    /// Şablonda A1 yoksa, ayarlardaki etiket mm ölçülerini SBPL A1 olarak ekler.
    /// Format (CL4NX): ESC A1 aaaabbbb — a=yükseklik(dot), b=genişlik(dot); veya VaaaaaHbbbb.
    /// </summary>
    private string EnsureMediaSize(string rendered)
    {
        if (string.IsNullOrEmpty(rendered))
        {
            return rendered;
        }

        // Zaten A1 varsa dokunma (admin şablonda override edebilir).
        if (rendered.Contains("\u001BA1", StringComparison.Ordinal) ||
            rendered.Contains("<ESC>A1", StringComparison.OrdinalIgnoreCase))
        {
            return rendered;
        }

        var dotsPerMm = _options.Dpi / 25.4;
        var heightDots = Math.Clamp((int)Math.Round(_options.LabelHeightMm * dotsPerMm), 50, 9999);
        var widthDots = Math.Clamp((int)Math.Round(_options.LabelWidthMm * dotsPerMm), 50, 9999);
        var a1 = $"\u001BA1V{heightDots:D5}H{widthDots:D5}\n";

        // ESC A'dan hemen sonra ekle.
        const string start = "\u001BA";
        var idx = rendered.IndexOf(start, StringComparison.Ordinal);
        if (idx < 0)
        {
            return a1 + rendered;
        }

        var insertAt = idx + start.Length;
        if (insertAt < rendered.Length && (rendered[insertAt] == '\r' || rendered[insertAt] == '\n'))
        {
            insertAt++;
            if (insertAt < rendered.Length && rendered[insertAt - 1] == '\r' && rendered[insertAt] == '\n')
            {
                insertAt++;
            }
        }

        return rendered.Insert(insertAt, a1);
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
        // Gerçek yazıcıya gönderilecek içerikle birebir aynı olsun diye (ı→i, ş→s vb.) burada da
        // ASCII'ye çevrilir; simülasyon dosyası fiziksel çıktının tam eşleniği olur.
        await File.WriteAllTextAsync(path, PrinterEncoding.ToAsciiSafe(rendered), Encoding.ASCII, cancellationToken);
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
            ["Time"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            ["Date"] = now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["QrCode"] = FirstNonEmpty(values, "MaterialNumber", "Barcode1", "Barcode3", "Barcode2"),
            ["VendorCode"] = values.GetValueOrDefault("VendorCode") ?? string.Empty
        };

        if (!enriched.ContainsKey("Year"))
        {
            enriched["Year"] = now.Year.ToString(CultureInfo.InvariantCulture);
        }

        // Marka adı: önce SAP barkod BEZEI, yoksa SAS satırındaki malzeme grubu açıklaması (Doqu/Bellona).
        // Kod (2Q9 vb.) yalnızca ikisi de boşsa basılır.
        if (string.IsNullOrWhiteSpace(enriched.GetValueOrDefault("BrandText")))
        {
            enriched["BrandText"] = enriched.GetValueOrDefault("Brand") ?? string.Empty;
        }

        return enriched;
    }

    private static string FirstNonEmpty(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static readonly Dictionary<string, string> BrandCodeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2Q9"] = "Doqu",
        ["2Q9A"] = "Doqu",
    };

    private static string ResolveBrandText(
        string? labelBrandText,
        string? materialGroupDescription,
        string? brandCode,
        string? materialNumber = null)
    {
        // MARKA_YAZI dolu ve koddan farklıysa kullan (SAP bazen koda aynı değeri yazar).
        if (!string.IsNullOrWhiteSpace(labelBrandText)
            && !labelBrandText.Trim().Equals(brandCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return labelBrandText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(materialGroupDescription)
            && !materialGroupDescription.Trim().Equals(brandCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return materialGroupDescription.Trim();
        }

        var code = (brandCode ?? string.Empty).Trim();
        if (code.Length == 0 && !string.IsNullOrWhiteSpace(materialNumber) && materialNumber.Length >= 3)
        {
            code = materialNumber.Trim()[..3];
        }

        if (BrandCodeNames.TryGetValue(code, out var mapped))
        {
            return mapped;
        }

        // 2Q9xxxx malzeme → Doqu
        if (!string.IsNullOrWhiteSpace(materialNumber)
            && materialNumber.StartsWith("2Q9", StringComparison.OrdinalIgnoreCase))
        {
            return "Doqu";
        }

        return code;
    }

    private static List<Dictionary<string, string>> BuildLabelItems(SasLabelPrintContext context)
    {
        var now = DateTime.Now;
        var calendar = CultureInfo.CurrentCulture.Calendar;
        var week = calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

        if (context.SapResult.Labels is { Count: > 0 } sapLabels)
        {
            return sapLabels.Select(label =>
            {
                var (barkod1, barkod2) = ResolveBarcodesForLabelType(context.LabelType, label, context.MaterialNumber);
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MaterialDescription"] = label.MaterialDescription,
                    ["MaterialNumber"] = label.MaterialNumber,
                    // SAP'nin ZMM_N_SAS_B yanıtındaki PAKET_MIKTARI (label.PackageQuantity) her zaman 0
                    // dönüyor; SAS listesinden alınıp doğrulanmış context.PackageQuantity kullanılır.
                    ["PackageQuantity"] = FormatQuantity(context.PackageQuantity),
                    ["Year"] = now.Year.ToString(CultureInfo.InvariantCulture),
                    ["Week"] = week.ToString(CultureInfo.InvariantCulture),
                    ["Barcode1"] = barkod1,
                    ["Barcode2"] = barkod2,
                    // Koli üstü @BARKOD3T/@BARKOD4T; SAP BARKOD3/4 boşsa tekil barkod alanları kullanılır.
                    ["Barcode3"] = string.IsNullOrWhiteSpace(label.Barcode3) ? barkod1 : label.Barcode3.Trim(),
                    ["Barcode4"] = string.IsNullOrWhiteSpace(label.Barcode4) ? barkod2 : label.Barcode4.Trim(),
                    ["Brand"] = label.BrandCode,
                    ["BrandText"] = ResolveBrandText(
                        label.BrandText,
                        context.MaterialGroupDescription,
                        string.IsNullOrWhiteSpace(label.BrandCode) ? context.MaterialGroupCode : label.BrandCode,
                        label.MaterialNumber),
                    // SAP SERNR alanı koli içi yanıtında her zaman boş geliyor; tekil kimlik BARKOD2'de.
                    ["SerialNumber"] = string.IsNullOrWhiteSpace(label.SerialNumber)
                        ? barkod2
                        : label.SerialNumber,
                    ["PurchaseOrderNo"] = context.PurchaseOrderNo,
                    ["LineNo"] = context.LineNo,
                    ["VendorCode"] = context.VendorCode ?? string.Empty
                };
            }).ToList();
        }

        return context.SapResult.BarcodeNumbers.Select((barcode, index) =>
        {
            var unique = barcode.Trim();
            // Koli içi referans: üst BARKOD1 = malzeme, alt BARKOD2 = tekil (X22… / MOCK…)
            var matnr = context.MaterialNumber?.Trim() ?? string.Empty;
            string barkod1;
            string barkod2;
            if (context.LabelType == LabelType.KoliIci && matnr.Length > 0)
            {
                barkod1 = matnr;
                barkod2 = unique.Equals(matnr, StringComparison.OrdinalIgnoreCase)
                    ? $"X22{matnr}-{index + 1:D4}"
                    : unique;
            }
            else
            {
                barkod1 = unique;
                barkod2 = matnr.Length > 0 ? $"X22{matnr}-{index + 1:D4}" : $"{unique}-2";
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MaterialDescription"] = context.MaterialDescription,
                ["MaterialNumber"] = context.MaterialNumber,
                ["PackageQuantity"] = FormatQuantity(context.PackageQuantity),
                ["Year"] = now.Year.ToString(CultureInfo.InvariantCulture),
                ["Week"] = week.ToString(CultureInfo.InvariantCulture),
                ["Barcode1"] = barkod1,
                ["Barcode2"] = barkod2,
                ["Barcode3"] = unique,
                ["Barcode4"] = barkod2,
                ["Brand"] = context.MaterialGroupCode ?? string.Empty,
                ["BrandText"] = ResolveBrandText(
                    null,
                    context.MaterialGroupDescription,
                    context.MaterialGroupCode,
                    context.MaterialNumber),
                ["SerialNumber"] = context.SerialNumber ?? string.Empty,
                ["PurchaseOrderNo"] = context.PurchaseOrderNo,
                ["LineNo"] = context.LineNo,
                ["VendorCode"] = context.VendorCode ?? string.Empty
            };
        }).ToList();
    }

    /// <summary>
    /// Koli içi referans etiket: üstte malzeme kodu (BARKOD1), altta tekil barkod (BARKOD2 / X22…).
    /// Koli üstü: mevcut tekil-öncelikli çözümleme.
    /// </summary>
    private static (string Barcode1, string Barcode2) ResolveBarcodesForLabelType(
        LabelType labelType,
        SasBarcodeLabelDto label,
        string? contextMaterialNumber)
    {
        if (labelType != LabelType.KoliIci)
        {
            return (ResolveLabelBarcode1(label), ResolveLabelBarcode2(label));
        }

        var matnr = !string.IsNullOrWhiteSpace(label.MaterialNumber)
            ? label.MaterialNumber.Trim()
            : (contextMaterialNumber?.Trim() ?? string.Empty);

        var unique = string.Empty;
        foreach (var candidate in new[] { label.Barcode2, label.Barcode1, label.Barcode3, label.Barcode4 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = candidate.Trim();
            if (matnr.Length > 0 && trimmed.Equals(matnr, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            unique = trimmed;
            break;
        }

        if (string.IsNullOrWhiteSpace(unique))
        {
            unique = FirstNonEmpty(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Barcode2"] = label.Barcode2 ?? string.Empty,
                    ["Barcode1"] = label.Barcode1 ?? string.Empty,
                    ["Barcode3"] = label.Barcode3 ?? string.Empty,
                    ["Barcode4"] = label.Barcode4 ?? string.Empty
                },
                "Barcode2", "Barcode1", "Barcode3", "Barcode4");
        }

        if (string.IsNullOrWhiteSpace(matnr))
        {
            matnr = unique;
        }

        return (matnr, unique);
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;

        // Uzun placeholder'lar önce (@BARKOD3T, @BARKOD3'ten önce) — aksi halde kısmi eşleşme bozar.
        foreach (var (placeholder, key) in Placeholders.OrderByDescending(p => p.Key.Length))
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

    /// <summary>
    /// Koli üstü @BARKOD1: tekil barkod öncelikli (SAP BARKOD1 malzeme ise BARKOD2 alınır).
    /// </summary>
    private static string ResolveLabelBarcode1(SasBarcodeLabelDto label)
    {
        if (IsMaterialCodeBarcode(label.Barcode1, label.MaterialNumber)
            && !string.IsNullOrWhiteSpace(label.Barcode2)
            && !IsMaterialCodeBarcode(label.Barcode2, label.MaterialNumber))
        {
            return label.Barcode2.Trim();
        }

        return label.Barcode1;
    }

    /// <summary>
    /// @BARKOD2: BARKOD1 malzeme koduysa onu ikinci barkod olarak bas (iki alan da dolu kalsın).
    /// </summary>
    private static string ResolveLabelBarcode2(SasBarcodeLabelDto label)
    {
        if (IsMaterialCodeBarcode(label.Barcode1, label.MaterialNumber)
            && !string.IsNullOrWhiteSpace(label.Barcode2)
            && !IsMaterialCodeBarcode(label.Barcode2, label.MaterialNumber))
        {
            return label.Barcode1.Trim();
        }

        return label.Barcode2;
    }

    private static bool IsMaterialCodeBarcode(string? barcode, string? materialNumber) =>
        !string.IsNullOrWhiteSpace(barcode)
        && !string.IsNullOrWhiteSpace(materialNumber)
        && barcode.Trim().Equals(materialNumber.Trim(), StringComparison.OrdinalIgnoreCase);
}
