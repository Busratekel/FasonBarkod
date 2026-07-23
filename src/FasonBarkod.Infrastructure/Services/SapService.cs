using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Infrastructure.Sap;
using FasonBarkod.Infrastructure.Sap.Rfc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Infrastructure.Services;

/// <summary>
/// SAP RFC çağrıları — yalnızca SAS (ZMM_N_SAS_*).
/// </summary>
public class SapService(
    ApplicationDbContext context,
    ISapConnectionFactory connectionFactory,
    IOptions<SapOptions> sapOptions,
    ILogger<SapService> logger) : ISapService
{
    private readonly SapOptions _sapOptions = sapOptions.Value;

    public async Task<SasListResult> ListSasAsync(
        string purchaseOrderNo,
        string? vendorCode = null,
        CancellationToken cancellationToken = default)
    {
        if (connectionFactory.IsConfigured)
        {
            try
            {
                var normalizedVendor = NormalizeVendorForSap(vendorCode);
                var paddedOrder = string.IsNullOrWhiteSpace(purchaseOrderNo)
                    ? "(boş)"
                    : SapAccountNumber.Pad10(purchaseOrderNo);

                if (string.IsNullOrWhiteSpace(purchaseOrderNo) && !string.IsNullOrWhiteSpace(normalizedVendor))
                {
                    var (vendorRows, vendorMessages) = InvokeSasListForVendor(normalizedVendor);
                    var vendorLines = MapSasLines(vendorRows, string.Empty, vendorCode);
                    return new SasListResult(vendorLines, vendorMessages);
                }

                logger.LogDebug(
                    "ZMM_N_SAS_L: EBELN={OrderNo} LIFNR={Vendor}",
                    paddedOrder,
                    normalizedVendor ?? "(boş)");

                var (exportRows, sapMessages) = InvokeSasListVariants(purchaseOrderNo, normalizedVendor);
                var lines = MapSasLines(exportRows, purchaseOrderNo, vendorCode);

                if (lines.Count > 0)
                {
                    return new SasListResult(lines, sapMessages);
                }

                logger.LogDebug(
                    "ZMM_N_SAS_L boş: {OrderNo}, satıcı={Vendor}, SAP={SapMessage}",
                    string.IsNullOrWhiteSpace(purchaseOrderNo) ? "(boş)" : purchaseOrderNo,
                    string.IsNullOrWhiteSpace(vendorCode) ? "(boş)" : vendorCode.Trim(),
                    sapMessages.Count > 0 ? string.Join(" | ", sapMessages) : "(yok)");

                if (!_sapOptions.UseMockFallback)
                {
                    return new SasListResult([], sapMessages);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ZMM_N_SAS_L hatası: {OrderNo}", purchaseOrderNo);

                if (!_sapOptions.UseMockFallback)
                {
                    throw;
                }
            }
        }
        else if (!_sapOptions.UseMockFallback)
        {
            return new SasListResult([], []);
        }

        var mockLines = await GetMockSasLinesAsync(purchaseOrderNo, cancellationToken);
        return new SasListResult(mockLines, []);
    }

    private (ZmmSasLExportRow[] Rows, List<string> Messages) InvokeSasListVariants(
        string purchaseOrderNo,
        string? vendorCode)
    {
        using var connection = connectionFactory.OpenConnection();

        var rfcNames = new[] { SapRfcFunctions.Sas.List, SapRfcFunctions.Sas.ListLegacy };
        var imports = new List<ZmmSasLImport>
        {
            ZmmSasLImportMapper.FromRequest(purchaseOrderNo, vendorCode)
        };

        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            imports.Add(ZmmSasLImportMapper.FromRequest(purchaseOrderNo, null));
        }

        var allMessages = new List<string>();

        foreach (var rfcName in rfcNames)
        {
            foreach (var import in imports)
            {
                try
                {
                    var (rows, messages) = InvokeSasListOnce(connection, import, rfcName);

                    foreach (var message in messages)
                    {
                        if (!allMessages.Contains(message, StringComparer.OrdinalIgnoreCase))
                        {
                            allMessages.Add(message);
                        }
                    }

                    if (messages.Count > 0)
                    {
                        logger.LogDebug("{Rfc} IT_HATA: {Messages}", rfcName, string.Join(" | ", messages));
                    }

                    if (rows.Length > 0)
                    {
                        return (rows, allMessages);
                    }
                }
                catch (Exception ex) when (IsMissingRfcMember(ex))
                {
                    logger.LogDebug("{Rfc} invoke atlandı: {Message}", rfcName, ex.Message);
                }
            }
        }

        return ([], allMessages);
    }

    /// <summary>
    /// SAP boş EBELN + I_KUNNR tek başına çalışmıyor; açık SAS listesindeki siparişler
    /// cari kodu ile tek tek (batch) sorgulanır — I_KUNNR filtre olarak kullanılır.
    /// </summary>
    private (ZmmSasLExportRow[] Rows, List<string> Messages) InvokeSasListForVendor(string vendorCode)
    {
        using var connection = connectionFactory.OpenConnection();
        const string rfcName = SapRfcFunctions.Sas.List;
        const int batchSize = 50;

        var openImport = ZmmSasLImportMapper.FromRequest(string.Empty, null);
        var (openRows, openMessages) = InvokeSasListOnce(connection, openImport, rfcName);

        var allMessages = new List<string>(openMessages);
        if (openRows.Length == 0)
        {
            return ([], allMessages);
        }

        var orderNos = openRows
            .Select(r => r.PurchaseOrderNo?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogDebug(
            "Cari filtresi: {OrderCount} açık SAS, I_KUNNR={Vendor}",
            orderNos.Count,
            vendorCode);

        var matched = new List<ZmmSasLExportRow>();

        for (var i = 0; i < orderNos.Count; i += batchSize)
        {
            var batch = orderNos.Skip(i).Take(batchSize).ToArray();
            var import = ZmmSasLImportMapper.FromOrders(batch, vendorCode);

            try
            {
                var (rows, messages) = InvokeSasListOnce(connection, import, rfcName);

                foreach (var message in messages)
                {
                    if (!allMessages.Contains(message, StringComparer.OrdinalIgnoreCase))
                    {
                        allMessages.Add(message);
                    }
                }

                if (rows.Length > 0)
                {
                    matched.AddRange(rows);
                }
            }
            catch (Exception ex) when (IsMissingRfcMember(ex))
            {
                logger.LogDebug("{Rfc} batch invoke atlandı: {Message}", rfcName, ex.Message);
            }
        }

        return (matched.ToArray(), allMessages);
    }

    private static (ZmmSasLExportRow[] Rows, List<string> Messages) InvokeSasListOnce(
        SapNwRfc.SapConnection connection,
        ZmmSasLImport import,
        string rfcName)
    {
        using var function = connection.CreateFunction(rfcName);
        var invoke = new ZmmSasLInvoke
        {
            PurchaseOrderNumbers = import.PurchaseOrderNumbers,
            VendorCode = import.VendorCode
        };

        var result = function.Invoke<ZmmSasLInvoke>(invoke);
        var messages = ExtractSapMessages(result.Errors);

        return (result.Items, messages);
    }

    private static List<string> ExtractSapMessages(ZmmSasLErrorRow[] errors) =>
        errors
            .Select(e => e.Message.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsMissingRfcMember(Exception ex) =>
        ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("FUNCTION_NOT_FOUND", StringComparison.OrdinalIgnoreCase);

    private static List<SasLineDto> MapSasLines(
        IEnumerable<ZmmSasLExportRow> exportRows,
        string purchaseOrderNo,
        string? vendorCode) =>
        exportRows.Select(r => new SasLineDto(
            string.IsNullOrWhiteSpace(r.PurchaseOrderNo) ? purchaseOrderNo : r.PurchaseOrderNo.Trim(),
            FormatLineNo(r.LineNo),
            r.MaterialNumber.Trim(),
            r.MaterialDescription.Trim(),
            string.IsNullOrWhiteSpace(r.Unit) ? "AD" : r.Unit.Trim(),
            r.Quantity,
            r.PackageQuantity > 0 ? r.PackageQuantity : 1,
            r.PrintedBoxCount,
            r.PrintedInsideBoxCount,
            r.MaterialGroupCode.Trim(),
            r.MaterialGroupDescription.Trim(),
            vendorCode?.Trim() ?? string.Empty,
            string.Empty)).ToList();

    private static string FormatLineNo(int lineNo) =>
        lineNo > 0 ? lineNo.ToString() : string.Empty;

    public Task<SapBarcodeResult> CreateBarcodeAsync(
        SapBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Module != BarcodeModuleType.Sas)
        {
            return Task.FromResult(new SapBarcodeResult(false, "Yalnızca SAS modülü destekleniyor.", [], null));
        }

        if (!PackageQuantityValidator.IsValidMultiple(request.PrintQuantity, request.PackageQuantity, request.LabelType))
        {
            return Task.FromResult(new SapBarcodeResult(
                false,
                PackageQuantityValidator.GetValidationError(request.LabelType),
                [],
                null));
        }

        const string rfcName = SapRfcFunctions.Sas.CreateBarcode;
        string? sapError = null;

        if (connectionFactory.IsConfigured)
        {
            try
            {
                using var connection = connectionFactory.OpenConnection();
                using var function = connection.CreateFunction(rfcName);

                var import = ZmmSasBImportMapper.FromBarcodeRequest(request);
                logger.LogDebug(
                    "ZMM_N_SAS_B: EBELN={OrderNo} EBELP={LineNo} BASILACAK={BarcodesToPrint}",
                    import.PurchaseOrderNo,
                    import.LineNo,
                    import.BarcodesToPrint);

                var result = function.Invoke<ZmmSasBInvoke>(import);

                var mapped = MapBarcodeResult(result.Items, result.Errors);
                if (mapped.Success)
                {
                    logger.LogDebug(
                        "ZMM_N_SAS_B: {OrderNo}/{LineNo} IT_DATA={Count}",
                        request.ReferenceNo,
                        request.LineNo,
                        result.Items.Length);
                    return Task.FromResult(mapped);
                }

                sapError = mapped.ErrorMessage;

                logger.LogDebug(
                    "ZMM_N_SAS_B boş: {OrderNo}/{LineNo}, SAP={SapError}",
                    request.ReferenceNo,
                    request.LineNo,
                    string.IsNullOrWhiteSpace(sapError) ? "(yok)" : sapError);

                if (!_sapOptions.UseMockFallback)
                {
                    return Task.FromResult(new SapBarcodeResult(
                        false,
                        BuildBarcodeFallbackMessage(rfcName, sapError),
                        [],
                        null));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Rfc} hatası", rfcName);
                sapError = ex.Message;

                if (!_sapOptions.UseMockFallback)
                {
                    return Task.FromResult(new SapBarcodeResult(false, ex.Message, [], null));
                }
            }
        }
        else if (!_sapOptions.UseMockFallback)
        {
            return Task.FromResult(new SapBarcodeResult(
                false,
                "SAP bağlantısı kapalı. Sap:Enabled=true ve User Secrets ayarlayın.",
                [],
                null));
        }

        var labelCount = PackageQuantityValidator.CalculateLabelCount(
            request.PrintQuantity,
            request.PackageQuantity,
            request.LabelType);

        // Saniye hassasiyetli zaman damgası tek başına yeterli değil: aynı saniye içinde
        // (örn. çift tıklama/yeniden gönderim) yapılan iki ayrı "yeni basım" isteği aynı MOCK
        // barkodu üretip BarcodePrints tablosunda gerçek çakışmaya yol açabiliyordu. Her çağrıya
        // özgü rastgele bir ek (Guid) ekleyerek bu çakışmayı pratikte imkansız hale getiriyoruz.
        var mockStamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var mockNonce = Guid.NewGuid().ToString("N")[..6];
        var orderKey = request.ReferenceNo.Trim();

        var fallbackMessage = connectionFactory.IsConfigured
            ? BuildBarcodeFallbackMessage(rfcName, sapError)
            : "SAP bağlantısı kapalı. Sap:Enabled=true ve User Secrets ayarlayın.";

        return Task.FromResult(new SapBarcodeResult(
            false,
            fallbackMessage,
            Enumerable.Range(1, labelCount)
                .Select(i => $"MOCK-{orderKey}-{mockStamp}-{mockNonce}-{i:D4}")
                .ToList(),
            null));
    }

    private static string BuildBarcodeFallbackMessage(string rfcName, string? sapError) =>
        !string.IsNullOrWhiteSpace(sapError)
            ? $"SAP: {sapError}"
            : $"SAP RFC yanıt vermedi ({rfcName}). Giriş değerlerini ve RFC alan adlarını doğrulayın.";

    public Task<SapBarcodeResult> ReprintBarcodeAsync(
        SapReprintRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Module != BarcodeModuleType.Sas)
        {
            return Task.FromResult(new SapBarcodeResult(false, "Yalnızca SAS modülü destekleniyor.", [], null));
        }

        const string rfcName = SapRfcFunctions.Sas.Reprint;

        if (connectionFactory.IsConfigured)
        {
            try
            {
                using var connection = connectionFactory.OpenConnection();
                using var function = connection.CreateFunction(rfcName);

                var result = function.Invoke<ZmmSasBtInvoke>(
                    ZmmSasBImportMapper.FromReprintRequest(request));

                var mapped = MapBarcodeResult(result.Items, result.Errors);
                if (mapped.Success)
                {
                    return Task.FromResult(mapped);
                }

                logger.LogDebug(
                    "ZMM_N_SAS_B_T boş: SERNR={Serial}, SAP={SapError}",
                    request.SerialNumber,
                    string.IsNullOrWhiteSpace(mapped.ErrorMessage) ? "(yok)" : mapped.ErrorMessage);

                if (!_sapOptions.UseMockFallback)
                {
                    return Task.FromResult(new SapBarcodeResult(
                        false,
                        $"SAP RFC yanıt vermedi ({rfcName}).",
                        [],
                        null));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Rfc} hatası", rfcName);

                if (!_sapOptions.UseMockFallback)
                {
                    return Task.FromResult(new SapBarcodeResult(false, ex.Message, [], null));
                }
            }
        }
        else if (!_sapOptions.UseMockFallback)
        {
            return Task.FromResult(new SapBarcodeResult(
                false,
                "SAP bağlantısı kapalı.",
                [],
                null));
        }

        return Task.FromResult(new SapBarcodeResult(
            false,
            connectionFactory.IsConfigured
                ? $"SAP RFC yanıt vermedi ({rfcName})."
                : "SAP bağlantısı kapalı.",
            [$"MOCK-R-{request.SerialNumber.Trim()}-{DateTime.UtcNow:yyyyMMddHHmmss}"],
            null));
    }

    private async Task<IReadOnlyList<SasLineDto>> GetMockSasLinesAsync(
        string purchaseOrderNo,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("SAS mock veri: {OrderNo}", purchaseOrderNo);

        return await context.SalesOrderLines
            .Where(x => x.SalesOrderNo == purchaseOrderNo)
            .Select(x => new SasLineDto(
                x.SalesOrderNo,
                x.Id.ToString(),
                x.MaterialCode,
                x.MaterialName ?? string.Empty,
                "AD",
                x.Quantity,
                10,
                0,
                0,
                string.Empty,
                string.Empty,
                x.CustomerCode ?? string.Empty,
                x.CustomerName ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    private static SapBarcodeResult MapBarcodeResult(ZmmSasBExportRow[] items, ZmmSasLErrorRow[] errors)
    {
        var messages = ZmmSasBResultHelper.ExtractMessages(errors);
        var sapError = messages.Count > 0 ? string.Join(" | ", messages) : string.Empty;
        var labels = items.Select(r => new SasBarcodeLabelDto(
            r.Barcode1.Trim(),
            r.Barcode2.Trim(),
            r.Barcode3.Trim(),
            r.Barcode4.Trim(),
            r.MaterialNumber.Trim(),
            r.MaterialDescription.Trim(),
            r.BrandCode.Trim(),
            r.SerialNumber.Trim(),
            r.BrandText.Trim(),
            r.PackageQuantity)).ToList();

        var barcodes = ExtractBarcodes(labels);
        if (barcodes.Count == 0)
        {
            var message = !string.IsNullOrWhiteSpace(sapError)
                ? sapError
                : "SAP barkod listesi boş döndü.";
            return new SapBarcodeResult(false, message, [], null, labels, labels.Count);
        }

        return new SapBarcodeResult(true, null, barcodes, null, labels, labels.Count);
    }

    private static IReadOnlyList<string> ExtractBarcodes(IEnumerable<SasBarcodeLabelDto> labels)
    {
        var list = new List<string>();

        foreach (var label in labels)
        {
            // Her etiket satırı için tek birincil barkod: BARKOD1 boş/malzeme kodu ise
            // sonraki alanlara bak (koli içi yanıtında BARKOD1 bazen MATNR geliyor).
            var primary = ResolvePrimaryBarcode(label);
            AddIfPresent(list, primary);
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Etiket satırındaki gerçek barkodu seçer. SAP bazen BARKOD1 alanına malzeme kodunu
    /// (MATNR) yazar; bu durumda BARKOD2/3/4'e düşülür.
    /// </summary>
    internal static string ResolvePrimaryBarcode(SasBarcodeLabelDto label)
    {
        foreach (var candidate in new[] { label.Barcode1, label.Barcode2, label.Barcode3, label.Barcode4 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = candidate.Trim();
            if (!string.IsNullOrWhiteSpace(label.MaterialNumber)
                && trimmed.Equals(label.MaterialNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return trimmed;
        }

        return string.Empty;
    }

    private static void AddIfPresent(List<string> list, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            list.Add(value.Trim());
        }
    }

    private static string? NormalizeVendorForSap(string? vendorCode)
    {
        if (string.IsNullOrWhiteSpace(vendorCode))
        {
            return null;
        }

        var trimmed = vendorCode.Trim();
        return trimmed is "C001" or "C002" ? null : trimmed;
    }
}
