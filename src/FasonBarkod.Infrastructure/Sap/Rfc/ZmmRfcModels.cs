using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Sap;
using SapNwRfc;

namespace FasonBarkod.Infrastructure.Sap.Rfc;

/// <summary>
/// ZMM_N_SAS_L import — Z_DOQU_BARKOD / ZMM14701 (IT dokümanı).
/// IS_EBELN tablo tipi (Z_TT_EBELN).
/// Cari kod iş anlamında LIFNR; RFC import parametresi I_KUNNR.
/// </summary>
public class ZmmSasLImport
{
    [SapName("IS_EBELN")]
    public ZmmEbelnRow[] PurchaseOrderNumbers { get; set; } = [];

    [SapName("I_KUNNR")]
    [SapBufferLength(10)]
    public string? VendorCode { get; set; }
}

/// <summary>
/// ZMM_N_SAS_L — IS_EBELN + I_KUNNR (giriş), IT_DATA (çıkış / ZMM14701), IT_HATA (hata mesajları).
/// IT_HATA'nın gerçek yapısı reflection ile doğrulandı: tek alan — MESAJ (CHAR). BAPIRET2 değil.
/// </summary>
public class ZmmSasLInvoke
{
    [SapName("IS_EBELN")]
    public ZmmEbelnRow[] PurchaseOrderNumbers { get; set; } = [];

    [SapName("I_KUNNR")]
    [SapBufferLength(10)]
    public string? VendorCode { get; set; }

    [SapName("IT_DATA")]
    public ZmmSasLExportRow[] Items { get; set; } = [];

    [SapName("IT_HATA")]
    public ZmmSasLErrorRow[] Errors { get; set; } = [];
}

public class ZmmSasLErrorRow
{
    [SapName("MESAJ")]
    public string Message { get; set; } = string.Empty;
}

public class ZmmEbelnRow
{
    [SapName("EBELN")]
    [SapBufferLength(10)]
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
    public int LineNo { get; set; }

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
            VendorCode = SapAccountNumber.Pad10OrNull(vendorCode) ?? SapAccountNumber.Char10Spaces()
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

    public static ZmmSasLImport FromOrders(IEnumerable<string> purchaseOrderNos, string? vendorCode = null)
    {
        var orders = purchaseOrderNos
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => SapAccountNumber.Pad10(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => new ZmmEbelnRow { PurchaseOrderNo = x })
            .ToArray();

        return new ZmmSasLImport
        {
            PurchaseOrderNumbers = orders,
            VendorCode = SapAccountNumber.Pad10OrNull(vendorCode) ?? SapAccountNumber.Char10Spaces()
        };
    }

    public static ZmmSasLInvoke ToInvoke(string purchaseOrderNo, string? vendorCode = null) =>
        new()
        {
            VendorCode = SapAccountNumber.Pad10OrNull(vendorCode) ?? SapAccountNumber.Char10Spaces(),
            PurchaseOrderNumbers = string.IsNullOrWhiteSpace(purchaseOrderNo)
                ? []
                : [new ZmmEbelnRow { PurchaseOrderNo = SapAccountNumber.Pad10(purchaseOrderNo) }]
        };
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

    [SapName("I_BASILACAK_BARKOD")]
    public int BarcodesToPrint { get; set; }
}
public class ZmmSasBInvoke
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

    [SapName("I_BASILACAK_BARKOD")]
    public int BarcodesToPrint { get; set; }

    [SapName("IT_DATA")]
    public ZmmSasBExportRow[] Items { get; set; } = [];

    [SapName("IT_HATA")]
    public ZmmSasLErrorRow[] Errors { get; set; } = [];
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
/// ZMM_N_SAS_B_T çağrısı için import + IT_DATA + IT_HATA'yı birleştiren tek model.
/// </summary>
public class ZmmSasBtInvoke
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

    [SapName("IT_DATA")]
    public ZmmSasBExportRow[] Items { get; set; } = [];

    [SapName("IT_HATA")]
    public ZmmSasLErrorRow[] Errors { get; set; } = [];
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

public static class ZmmSasBResultHelper
{
    public static IEnumerable<ZmmSasBExportRow> AllItems(ZmmSasBInvoke result) => result.Items;

    public static IEnumerable<ZmmSasBExportRow> AllItems(ZmmSasBtInvoke result) => result.Items;

    public static List<string> ExtractMessages(ZmmSasLErrorRow[] errors) =>
        errors
            .Select(e => e.Message.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public static class ZmmSasBImportMapper
{
    public static ZmmSasBInvoke FromBarcodeRequest(SapBarcodeRequest request)
    {
        var isBoxTop = request.LabelType == LabelType.KoliUstu;
        var printQty = (int)request.PrintQuantity;
        var packageQty = (int)request.PackageQuantity;
        // I_BASILACAK_BARKOD = üretilecek etiket/barkod adedi.
        // Koli üstü: basım miktarı ÷ paket (örn. 10/5 → 2 etiket).
        // Koli içi: basım miktarı (örn. 5 → 5 etiket).
        // Eskiden printQty gönderiliyordu; koli üstünde SAP'e 10 gidince tek satır dönüyordu.
        var barcodesToPrint = PackageQuantityValidator.CalculateLabelCount(
            request.PrintQuantity,
            request.PackageQuantity,
            request.LabelType);

        return new ZmmSasBInvoke
        {
            PurchaseOrderNo = SapAccountNumber.Pad10(request.ReferenceNo),
            LineNo = ParseLineNo(request.LineNo),
            BoxTopPrintQuantity = isBoxTop ? printQty : 0,
            BoxInsidePrintQuantity = isBoxTop ? 0 : printQty,
            PackageInsideQuantity = packageQty,
            PrintBoxTop = isBoxTop ? "X" : string.Empty,
            PrintBoxInside = isBoxTop ? string.Empty : "X",
            BarcodesToPrint = barcodesToPrint
        };
    }

    public static ZmmSasBtInvoke FromReprintRequest(SapReprintRequest request)
    {
        var isBoxTop = request.LabelType == LabelType.KoliUstu;

        return new ZmmSasBtInvoke
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
