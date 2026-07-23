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

    [Display(Name = "Basılacak Koli Üstü Miktar")]
    public decimal KoliUstuQuantity { get; set; }

    [Display(Name = "Basılacak Koli İçi Miktar")]
    public decimal KoliIciQuantity { get; set; }

    [Display(Name = "Seri No (SERNR)")]
    public string SerialNumber { get; set; } = string.Empty;

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
        if (string.IsNullOrWhiteSpace(VendorCode) && !string.IsNullOrWhiteSpace(line.VendorCode))
        {
            VendorCode = line.VendorCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(line.CustomerName))
        {
            CustomerName = line.CustomerName.Trim();
        }

        if (KoliUstuQuantity <= 0)
        {
            KoliUstuQuantity = line.PackageQuantity > 0 ? line.PackageQuantity : 1;
        }

        if (KoliIciQuantity <= 0)
        {
            KoliIciQuantity = line.PackageQuantity > 0 ? line.PackageQuantity : 1;
        }
    }
}
