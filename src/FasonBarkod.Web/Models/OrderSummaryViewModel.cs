namespace FasonBarkod.Web.Models;

public class OrderSummaryViewModel
{
    public string SalesOrderNo { get; set; } = string.Empty;

    public string? CustomerName { get; set; }

    public DateTime? OrderDate { get; set; }

    public int LineCount { get; set; }

    public decimal TotalQuantity { get; set; }
}
