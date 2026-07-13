using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Sap;
using SapNwRfc;

namespace FasonBarkod.Infrastructure.Sap.Rfc;

/// <summary>
/// ZMM_N_SAS_L import — Z_DOQU_BARKOD / ZMM14701 (IT dokümanı).
/// IS_EBELN tablo tipi (Z_TT_EBELN), I_KUNNR satıcı kodu.
/// </summary>
public class ZmmSasLImport
{
    [SapName("IS_EBELN")]
    public ZmmEbelnRow[] PurchaseOrderNumbers { get; set; } = [];

    [SapName("I_KUNNR")]
    public string? VendorCode { get; set; }
}

public class ZmmEbelnRow
{
    [SapName("EBELN")]
    public string PurchaseOrderNo { get; set; } = string.Empty;
}

/// <summary>
/// ZMM14701 / IT_DATA satır yapısı.
/// </summary>
public class ZmmSasLExportRow
{
    [SapName("EBELN")]
    public string PurchaseOrderNo { get; set; } = string.Empty;

    [SapName("EBELP")]
    public string LineNo { get; set; } = string.Empty;

    [SapName("MATNR")]
    public string MaterialNumber { get; set; } = string.Empty;

    [SapName("MAKTX")]
    public string MaterialDescription { get; set; } = string.Empty;

    [SapName("WERKS")]
    public string Plant { get; set; } = string.Empty;

    [SapName("MENGE")]
    public decimal Quantity { get; set; }

    [SapName("MEINS")]
    public string Unit { get; set; } = string.Empty;

    [SapName("PAKET_MIKTARI")]
    public int PackageQuantity { get; set; }

    [SapName("BASILAN_KOLI")]
    public int PrintedBoxCount { get; set; }

    [SapName("BASILAN_KOLI_ICI")]
    public int PrintedInsideBoxCount { get; set; }

    [SapName("MVGR1")]
    public string MaterialGroupCode { get; set; } = string.Empty;

    [SapName("BEZEI")]
    public string MaterialGroupDescription { get; set; } = string.Empty;
}

public class ZmmSasLResult
{
    [SapName("IT_DATA")]
    public ZmmSasLExportRow[] Items { get; set; } = [];
}

/// <summary>
/// Bazı sistemlerde liste çıktısı ET_DATA tablosunda döner.
/// </summary>
public class ZmmSasLResultEt
{
    [SapName("ET_DATA")]
    public ZmmSasLExportRow[] Items { get; set; } = [];
}

public static class ZmmSasLResultHelper
{
    public static ZmmSasLExportRow[] ResolveItems(ZmmSasLResult primary, ZmmSasLResultEt? alternate) =>
        primary.Items.Length > 0
            ? primary.Items
            : alternate?.Items ?? [];
}

public static class ZmmSasLImportMapper
{
    public static ZmmSasLImport FromRequest(string purchaseOrderNo, string? vendorCode = null)
    {
        var import = new ZmmSasLImport
        {
            VendorCode = SapAccountNumber.Pad10OrNull(vendorCode)
        };

        if (!string.IsNullOrWhiteSpace(purchaseOrderNo))
        {
            import.PurchaseOrderNumbers =
            [
                new ZmmEbelnRow { PurchaseOrderNo = SapAccountNumber.Pad10(purchaseOrderNo) }
            ];
        }

        return import;
    }
}

/// <summary>
/// ZMM_N_SAS_B import — barcode.doqu.com.tr GetSAPSASBarcodePrint → ZMM_N_SAS_B.
/// </summary>
public class ZmmSasBImport
{
    [SapName("I_EBELN")]
    public string PurchaseOrderNo { get; set; } = string.Empty;

    [SapName("I_EBELP")]
    public int LineNo { get; set; }

    [SapName("I_KOLI_BAS_MIK")]
    public int BoxTopPrintQuantity { get; set; }

    [SapName("I_KOLI_ICI_BAS_MIK")]
    public int BoxInsidePrintQuantity { get; set; }

    [SapName("I_PAKET_ICI")]
    public int PackageInsideQuantity { get; set; }

    [SapName("I_KOLI")]
    public string PrintBoxTop { get; set; } = string.Empty;

    [SapName("I_KOLI_ICI")]
    public string PrintBoxInside { get; set; } = string.Empty;
}

/// <summary>
/// ZMM_N_SAS_B_T import — GetSAPSASBarcodeReprint.
/// </summary>
public class ZmmSasBtImport
{
    [SapName("I_EBELN")]
    public string PurchaseOrderNo { get; set; } = string.Empty;

    [SapName("I_EBELP")]
    public int LineNo { get; set; }

    [SapName("I_PAKET_ICI")]
    public int PackageInsideQuantity { get; set; }

    [SapName("I_KOLI")]
    public string PrintBoxTop { get; set; } = string.Empty;

    [SapName("I_KOLI_ICI")]
    public string PrintBoxInside { get; set; } = string.Empty;

    [SapName("I_SERNR")]
    public string SerialNumber { get; set; } = string.Empty;
}

/// <summary>
/// ZPP14998 / IT_DATA — etiket basım çıktısı.
/// </summary>
public class ZmmSasBExportRow
{
    [SapName("BARKOD1")]
    public string Barcode1 { get; set; } = string.Empty;

    [SapName("BARKOD2")]
    public string Barcode2 { get; set; } = string.Empty;

    [SapName("BARKOD3")]
    public string Barcode3 { get; set; } = string.Empty;

    [SapName("BARKOD4")]
    public string Barcode4 { get; set; } = string.Empty;

    [SapName("ZMARKA")]
    public string BrandCode { get; set; } = string.Empty;

    [SapName("MATNR")]
    public string MaterialNumber { get; set; } = string.Empty;

    [SapName("MAKTX")]
    public string MaterialDescription { get; set; } = string.Empty;

    [SapName("SERNR")]
    public string SerialNumber { get; set; } = string.Empty;

    [SapName("MARKA_YAZI")]
    public string BrandText { get; set; } = string.Empty;

    [SapName("PAKET_MIKTARI")]
    public int PackageQuantity { get; set; }

    [SapName("MVGR1")]
    public string MaterialGroupCode { get; set; } = string.Empty;

    [SapName("BEZEI")]
    public string MaterialGroupDescription { get; set; } = string.Empty;
}

public class ZmmSasBResult
{
    [SapName("IT_DATA")]
    public ZmmSasBExportRow[] Items { get; set; } = [];

    [SapName("E_ERROR_MESSAGE")]
    public string ErrorMessage { get; set; } = string.Empty;
}

public static class ZmmSasBResultHelper
{
    public static IEnumerable<ZmmSasBExportRow> AllItems(ZmmSasBResult result) => result.Items;

    public static string ResolvedErrorMessage(ZmmSasBResult result) =>
        !string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.ErrorMessage.Trim() : string.Empty;
}

public static class ZmmSasBImportMapper
{
    public static ZmmSasBImport FromBarcodeRequest(SapBarcodeRequest request)
    {
        var isBoxTop = request.LabelType == LabelType.KoliUstu;
        var printQty = (int)request.PrintQuantity;
        var packageQty = (int)request.PackageQuantity;

        return new ZmmSasBImport
        {
            PurchaseOrderNo = SapAccountNumber.Pad10(request.ReferenceNo),
            LineNo = ParseLineNo(request.LineNo),
            BoxTopPrintQuantity = isBoxTop ? printQty : 0,
            BoxInsidePrintQuantity = isBoxTop ? 0 : printQty,
            PackageInsideQuantity = packageQty,
            PrintBoxTop = isBoxTop ? "X" : string.Empty,
            PrintBoxInside = isBoxTop ? string.Empty : "X"
        };
    }

    public static ZmmSasBtImport FromReprintRequest(SapReprintRequest request)
    {
        var isBoxTop = request.LabelType == LabelType.KoliUstu;

        return new ZmmSasBtImport
        {
            PurchaseOrderNo = SapAccountNumber.Pad10(request.PurchaseOrderNo),
            LineNo = ParseLineNo(request.LineNo),
            PackageInsideQuantity = (int)request.PackageQuantity,
            PrintBoxTop = isBoxTop ? "X" : string.Empty,
            PrintBoxInside = isBoxTop ? string.Empty : "X",
            SerialNumber = request.SerialNumber.Trim()
        };
    }

    private static int ParseLineNo(string lineNo) =>
        int.TryParse(lineNo.Trim(), out var parsed) ? parsed : 0;
}
