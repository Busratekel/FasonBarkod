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

    public string MaterialCode { get; set; } = string.Empty;

    public string? MaterialName { get; set; }

    public decimal Quantity { get; set; }

    public DateTime PrintDate { get; set; }

    public string? PrintedBy { get; set; }

    public BarcodePrintStatus Status { get; set; }

    public bool SapSent { get; set; }
}
