using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Sap;

public record SasLineDto(
    string PurchaseOrderNo,
    string LineNo,
    string MaterialNumber,
    string MaterialDescription,
    string Unit,
    decimal Quantity,
    decimal PackageQuantity,
    int PrintedBoxCount,
    int PrintedInsideBoxCount = 0,
    string MaterialGroupCode = "",
    string MaterialGroupDescription = "",
    string VendorCode = "",
    string CustomerName = "");

/// <summary>
/// ZPP14998 — etiket basım çıktısı (GetSAPSASBarcodePrint SOAP).
/// </summary>
public record SasBarcodeLabelDto(
    string Barcode1,
    string Barcode2,
    string Barcode3,
    string Barcode4,
    string MaterialNumber,
    string MaterialDescription,
    string BrandCode,
    string SerialNumber,
    string BrandText,
    int PackageQuantity);

public record SapBarcodeRequest(
    BarcodeModuleType Module,
    LabelType LabelType,
    string ReferenceNo,
    string LineNo,
    string MaterialNumber,
    decimal PackageQuantity,
    decimal PrintQuantity,
    string? BrandCode,
    string? VendorCode = null);

public record SapBarcodeResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<string> BarcodeNumbers,
    string? PrintTemplatePath,
    IReadOnlyList<SasBarcodeLabelDto>? Labels = null,
    int PrintBarcodeQuantity = 0);

public record SapReprintRequest(
    BarcodeModuleType Module,
    string PurchaseOrderNo,
    string LineNo,
    decimal PackageQuantity,
    LabelType LabelType,
    string SerialNumber,
    string? VendorCode = null);

/// <summary>ZMMIST14000 — GetSAPSASBarcodeSerials satırı.</summary>
public record SasSerialDto(
    string PurchaseOrderNo,
    string LineNo,
    string SerialNumber,
    decimal Quantity,
    string Unit,
    string Status,
    string BoxFlag);

public record SasSerialListResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<SasSerialDto> Serials);
