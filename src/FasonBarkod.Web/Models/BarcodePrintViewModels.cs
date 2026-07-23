using System.ComponentModel.DataAnnotations;
using FasonBarkod.Core.Enums;

namespace FasonBarkod.Web.Models;

public class CreateBarcodePrintViewModel
{
    public int OrderLineId { get; set; }

    public string SalesOrderNo { get; set; } = string.Empty;

    public string MaterialCode { get; set; } = string.Empty;

    public string? MaterialName { get; set; }

    public decimal MaxQuantity { get; set; }

    [Required(ErrorMessage = "Adet zorunludur.")]
    [Range(0.001, double.MaxValue, ErrorMessage = "Adet 0'dan büyük olmalıdır.")]
    [Display(Name = "Etiket Adedi")]
    public decimal Quantity { get; set; } = 1;
}

public class BarcodePrintListItemViewModel
{
    public int Id { get; set; }

    public string BarcodeNo { get; set; } = string.Empty;

    public string SalesOrderNo { get; set; } = string.Empty;

    public string? LineNo { get; set; }

    public string? VendorCode { get; set; }

    public LabelType? LabelType { get; set; }

    public string MaterialCode { get; set; } = string.Empty;

    public string? MaterialName { get; set; }

    public decimal Quantity { get; set; }

    public DateTime PrintDate { get; set; }

    public string? PrintedBy { get; set; }

    public string? SerialNumber { get; set; }

    public BarcodePrintStatus Status { get; set; }

    public bool SapSent { get; set; }
}

public class BarcodePrintIndexViewModel
{
    public List<BarcodePrintListItemViewModel> Items { get; set; } = [];

    public string? VendorCode { get; set; }

    public string? PurchaseOrderNo { get; set; }

    public bool VendorCodeLocked { get; set; }

    public string Sort { get; set; } = "date";

    public string SortDir { get; set; } = "desc";

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 50;

    public int TotalCount { get; set; }

    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling(TotalCount / (double)PageSize)
        : 0;
}

public class ConfirmQzPrintRequest
{
    public List<int> Ids { get; set; } = [];

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }
}
