using System.ComponentModel.DataAnnotations;
using FasonBarkod.Core.Sap;

namespace FasonBarkod.Web.Models;

public class SasSearchViewModel
{
    [Display(Name = "SAS Numarası")]
    public string? PurchaseOrderNo { get; set; }

    [Display(Name = "Satıcı Kodu")]
    public string? VendorCode { get; set; }

    /// <summary>
    /// Operatör hesabında satıcı kodu kilitli (kullanıcı kartından gelir).
    /// </summary>
    public bool VendorCodeLocked { get; set; }

    /// <summary>
    /// SAP açık listesinde LIFNR yok; yalnızca satıcı bilindiğinde sütun gösterilir.
    /// </summary>
    public bool ShowVendorColumn { get; set; }

    /// <summary>
    /// SAS no boş bırakıldığında SAP'ten dönen sipariş özeti (sayfalanmış).
    /// </summary>
    public List<SasOrderSummaryViewModel> Orders { get; set; } = [];

    public string? ListMessage { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public int TotalCount { get; set; }

    public int TotalPages =>
        PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public class SasOrderSummaryViewModel
{
    public string PurchaseOrderNo { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public string? SampleMaterial { get; set; }
    public string? VendorCode { get; set; }
}

public class SasDetailViewModel
{
    public string PurchaseOrderNo { get; set; } = string.Empty;

    public string? VendorCode { get; set; }

    public string? CustomerName { get; set; }

    public string ActiveTab { get; set; } = "print";

    public List<SasLineDto> Lines { get; set; } = [];

    public string? SelectedLineNo { get; set; }

    public string LineNo { get; set; } = string.Empty;

    public string MaterialNumber { get; set; } = string.Empty;

    public string MaterialDescription { get; set; } = string.Empty;

    [Display(Name = "Paket Miktar")]
    public decimal PackageQuantity { get; set; }

    /// <summary>SAP satırındaki orijinal PAKET_MIKTARI (üst sınır).</summary>
    public decimal OriginalPackageQuantity { get; set; }

    /// <summary>SAP MENGE — koli üstü basım üst sınırı.</summary>
    public decimal OrderQuantity { get; set; }

    [Display(Name = "Basılacak Koli Üstü Miktar")]
    public decimal KoliUstuQuantity { get; set; }

    [Display(Name = "Basılacak Koli İçi Miktar")]
    public decimal KoliIciQuantity { get; set; }

    [Display(Name = "Seri No (SERNR)")]
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>SAP ZMMIST14000 — koli üstü serileri.</summary>
    public List<SasSerialDto> SapSerialsBoxTop { get; set; } = [];

    /// <summary>SAP ZMMIST14000 — koli içi serileri.</summary>
    public List<SasSerialDto> SapSerialsBoxInside { get; set; } = [];

    public string? SapSerialsError { get; set; }

    public int SapSerialsCount => SapSerialsBoxTop.Count + SapSerialsBoxInside.Count;

    public List<BarcodePrintListItemViewModel> RecentPrints { get; set; } = [];

    public SasLineDto? SelectedLine =>
        Lines.FirstOrDefault(l => l.LineNo == SelectedLineNo);

    public void SelectLine(SasLineDto line)
    {
        SelectedLineNo = line.LineNo;
        LineNo = line.LineNo;
        MaterialNumber = line.MaterialNumber;
        MaterialDescription = line.MaterialDescription;
        PackageQuantity = line.PackageQuantity;
        OriginalPackageQuantity = line.PackageQuantity;
        OrderQuantity = line.Quantity;
        if (string.IsNullOrWhiteSpace(VendorCode) && !string.IsNullOrWhiteSpace(line.VendorCode))
        {
            VendorCode = line.VendorCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(line.CustomerName))
        {
            CustomerName = line.CustomerName.Trim();
        }

        // Doqu: paket düşürülebilir. MENGE < SAP paket ise (örn. 1/5) otomatik düşür;
        // aksi halde koli üstü katı olamaz, koli içi SAP Max:0 verir.
        if (line.Quantity > 0 && PackageQuantity > line.Quantity)
        {
            PackageQuantity = line.Quantity;
        }

        // Doqu: koli üstü varsayılan = MENGE (paket katıysa), değilse en büyük kat ≤ MENGE.
        if (KoliUstuQuantity <= 0 && PackageQuantity > 0 && line.Quantity > 0)
        {
            var menge = line.Quantity;
            var paket = PackageQuantity;
            if (menge % paket == 0)
            {
                KoliUstuQuantity = menge;
            }
            else
            {
                var multiples = Math.Floor(menge / paket) * paket;
                KoliUstuQuantity = multiples > 0 ? multiples : 0;
            }
        }

        // Doqu: koli içi MENGE/kat zorunlu değil; varsayılan 1.
        if (KoliIciQuantity <= 0)
        {
            KoliIciQuantity = 1;
        }
    }
}
