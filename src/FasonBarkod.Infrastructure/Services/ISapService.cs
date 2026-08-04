using FasonBarkod.Core.Sap;

namespace FasonBarkod.Infrastructure.Services;

/// <summary>
/// SAP kalemleri ve SAP'nin IT_HATA tablosuna yazdığı mesajlar (varsa).
/// </summary>
public record SasListResult(IReadOnlyList<SasLineDto> Lines, IReadOnlyList<string> SapMessages);

public interface ISapService
{
    Task<SasListResult> ListSasAsync(
        string purchaseOrderNo,
        string? vendorCode = null,
        CancellationToken cancellationToken = default);

    Task<SapBarcodeResult> CreateBarcodeAsync(
        SapBarcodeRequest request,
        CancellationToken cancellationToken = default);

    Task<SapBarcodeResult> ReprintBarcodeAsync(
        SapReprintRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Basılmış SERNR listesi (ZMMIST14000) — GetSAPSASBarcodeSerials / ZMM_N_SAS_S.
    /// </summary>
    Task<SasSerialListResult> ListSasSerialsAsync(
        string purchaseOrderNo,
        string lineNo,
        bool boxInside,
        CancellationToken cancellationToken = default);
}
