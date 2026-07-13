namespace FasonBarkod.Core.Entities;

/// <summary>
/// SAP'den çekilen sipariş kalemlerinin yerel önbelleği. Alanlar SAP netleşince güncellenecek.
/// </summary>
public class SalesOrderLine
{
    public int Id { get; set; }

    public string SalesOrderNo { get; set; } = string.Empty;

    public string? CustomerCode { get; set; }

    public string? CustomerName { get; set; }

    public string MaterialCode { get; set; } = string.Empty;

    public string? MaterialName { get; set; }

    public string? Color { get; set; }

    public string? BatchNo { get; set; }

    public decimal Quantity { get; set; }

    public DateTime? OrderDate { get; set; }

    public DateTime? SapUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
