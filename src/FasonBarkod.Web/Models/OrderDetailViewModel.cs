using FasonBarkod.Core.Entities;

namespace FasonBarkod.Web.Models;

public class OrderDetailViewModel
{
    public string SalesOrderNo { get; set; } = string.Empty;

    public string? CustomerName { get; set; }

    public DateTime? OrderDate { get; set; }

    public List<SalesOrderLine> Lines { get; set; } = [];
}
