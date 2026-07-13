using System.ComponentModel.DataAnnotations;
using FasonBarkod.Core.Sap;

namespace FasonBarkod.Web.Models;

public class SasSearchViewModel
{
    [Required(ErrorMessage = "SAS numarası zorunludur.")]
    [Display(Name = "SAS Numarası")]
    public string PurchaseOrderNo { get; set; } = string.Empty;

    [Display(Name = "Satıcı Kodu")]
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
