using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;

namespace FasonBarkod.Infrastructure.Services;

public interface ILabelPrintService
{
    Task<LabelPrintResult> PrintSasLabelsAsync(
        SasLabelPrintContext context,
        CancellationToken cancellationToken = default);
}

public record SasLabelPrintContext(
    LabelType LabelType,
    string PurchaseOrderNo,
    string LineNo,
    string MaterialNumber,
    string MaterialDescription,
    string? MaterialGroupCode,
    decimal PackageQuantity,
    SapBarcodeResult SapResult,
    string? SerialNumber = null,
    string? PrinterName = null,
    string? TemplateFileName = null,
    string? VendorCode = null,
    string? MaterialGroupDescription = null);

public record LabelPrintResult(
    bool Success,
    int PrintedCount,
    string? ErrorMessage,
    string? SimulatedOutputFolder = null,
    /// <summary>QZ Tray için üretilen SBPL içerikleri (sunucu yazıcıya göndermez).</summary>
    IReadOnlyList<string>? RawJobs = null,
    bool UseQzTray = false);
