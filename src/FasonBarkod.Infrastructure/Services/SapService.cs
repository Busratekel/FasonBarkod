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

    public async Task<IReadOnlyList<SasLineDto>> ListSasAsync(
        string purchaseOrderNo,
        string? vendorCode = null,
        CancellationToken cancellationToken = default)
    {
        if (connectionFactory.IsConfigured)
        {
            try
            {
                var normalizedVendor = NormalizeVendorForSap(vendorCode);
                var paddedOrder = SapAccountNumber.Pad10(purchaseOrderNo);

                logger.LogInformation(
                    "ZMM_N_SAS_L çağrılıyor: EBELN={OrderNo} KUNNR={Vendor}",
                    paddedOrder,
                    normalizedVendor ?? "(boş)");

                var exportRows = InvokeSasListVariants(purchaseOrderNo, normalizedVendor);
                var lines = MapSasLines(exportRows, purchaseOrderNo, vendorCode);

                if (lines.Count > 0)
                {
                    logger.LogInformation("ZMM_N_SAS_L başarılı: {OrderNo}, {Count} kalem", purchaseOrderNo, lines.Count);
                    return lines;
                }

                logger.LogWarning(
                    "ZMM_N_SAS_L boş döndü: {OrderNo}, satıcı={Vendor}",
                    purchaseOrderNo,
                    string.IsNullOrWhiteSpace(vendorCode) ? "(boş)" : vendorCode.Trim());

                if (!_sapOptions.UseMockFallback)
                {
                    return [];
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
            return [];
        }

        return await GetMockSasLinesAsync(purchaseOrderNo, cancellationToken);
    }

    private ZmmSasLExportRow[] InvokeSasListVariants(string purchaseOrderNo, string? vendorCode)
    {
        using var connection = connectionFactory.OpenConnection();

        var imports = new List<ZmmSasLImport>
        {
            ZmmSasLImportMapper.FromRequest(purchaseOrderNo, vendorCode)
        };

        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            imports.Add(ZmmSasLImportMapper.FromRequest(purchaseOrderNo, null));
        }

        foreach (var import in imports)
        {
            var rows = InvokeSasListOnce(connection, import);
            if (rows.Length > 0)
            {
                return rows;
            }
        }

        return [];
    }

    private ZmmSasLExportRow[] InvokeSasListOnce(
        SapNwRfc.SapConnection connection,
        ZmmSasLImport import)
    {
        using var function = connection.CreateFunction(SapRfcFunctions.Sas.List);
        var result = function.Invoke<ZmmSasLResult>(import);

        var exportRows = result.Items;
        if (exportRows.Length == 0)
        {
            try
            {
                using var altFunction = connection.CreateFunction(SapRfcFunctions.Sas.List);
                var alt = altFunction.Invoke<ZmmSasLResultEt>(import);
                exportRows = alt.Items;
            }
            catch (Exception ex) when (ex.Message.Contains("ET_DATA", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("ZMM_N_SAS_L ET_DATA yok, yalnızca IT_DATA kullanılıyor");
            }
        }

        return exportRows;
    }

    private static List<SasLineDto> MapSasLines(
        IEnumerable<ZmmSasLExportRow> exportRows,
        string purchaseOrderNo,
        string? vendorCode) =>
        exportRows.Select(r => new SasLineDto(
            string.IsNullOrWhiteSpace(r.PurchaseOrderNo) ? purchaseOrderNo : r.PurchaseOrderNo.Trim(),
            r.LineNo.Trim(),
            r.MaterialNumber.Trim(),
            r.MaterialDescription.Trim(),
            string.IsNullOrWhiteSpace(r.Unit) ? "AD" : r.Unit.Trim(),
            r.Quantity,
            r.PackageQuantity > 0 ? r.PackageQuantity : 1,
            r.PrintedBoxCount,
            r.MaterialGroupCode.Trim(),
            r.MaterialGroupDescription.Trim(),
            vendorCode?.Trim() ?? string.Empty,
            string.Empty)).ToList();

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
                PackageQuantityValidator.NotMultipleError,
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
                logger.LogInformation(
                    "ZMM_N_SAS_B çağrılıyor: EBELN={OrderNo} EBELP={LineNo} KOLI_BAS={BoxTop} KOLI_ICI_BAS={BoxInside} PAKET={Package} KOLI={Koli} KOLI_ICI={KoliIci}",
                    import.PurchaseOrderNo,
                    import.LineNo,
                    import.BoxTopPrintQuantity,
                    import.BoxInsidePrintQuantity,
                    import.PackageInsideQuantity,
                    import.PrintBoxTop,
                    import.PrintBoxInside);

                var result = function.Invoke<ZmmSasBResult>(import);

                var mapped = MapBarcodeResult(result);
                if (mapped.Success)
                {
                    return Task.FromResult(mapped);
                }

                sapError = ZmmSasBResultHelper.ResolvedErrorMessage(result);
                if (string.IsNullOrWhiteSpace(sapError))
                {
                    sapError = mapped.ErrorMessage;
                }

                logger.LogWarning(
                    "ZMM_N_SAS_B boş döndü: {OrderNo} kalem {LineNo}, satır={Count}, SAP={SapError}",
                    request.ReferenceNo,
                    request.LineNo,
                    ZmmSasBResultHelper.AllItems(result).Count(),
                    string.IsNullOrWhiteSpace(sapError) ? "(mesaj yok)" : sapError);

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

        var mockStamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var orderKey = request.ReferenceNo.Trim();

        var fallbackMessage = connectionFactory.IsConfigured
            ? BuildBarcodeFallbackMessage(rfcName, sapError)
            : "SAP bağlantısı kapalı. Sap:Enabled=true ve User Secrets ayarlayın.";

        return Task.FromResult(new SapBarcodeResult(
            false,
            fallbackMessage,
            Enumerable.Range(1, labelCount)
                .Select(i => $"MOCK-{orderKey}-{mockStamp}-{i:D4}")
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

                var result = function.Invoke<ZmmSasBResult>(
                    ZmmSasBImportMapper.FromReprintRequest(request));

                var mapped = MapBarcodeResult(result);
                if (mapped.Success)
                {
                    return Task.FromResult(mapped);
                }

                logger.LogWarning("ZMM_N_SAS_B_T boş döndü: SERNR={Serial}", request.SerialNumber);

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
        logger.LogInformation("SAS mock veri kullanılıyor: {OrderNo}", purchaseOrderNo);

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
                string.Empty,
                string.Empty,
                x.CustomerCode ?? string.Empty,
                x.CustomerName ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    private static SapBarcodeResult MapBarcodeResult(ZmmSasBResult result)
    {
        var sapError = ZmmSasBResultHelper.ResolvedErrorMessage(result);
        var labels = ZmmSasBResultHelper.AllItems(result).Select(r => new SasBarcodeLabelDto(
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
            AddIfPresent(list, label.Barcode1);
            AddIfPresent(list, label.Barcode2);
            AddIfPresent(list, label.Barcode3);
            AddIfPresent(list, label.Barcode4);
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
