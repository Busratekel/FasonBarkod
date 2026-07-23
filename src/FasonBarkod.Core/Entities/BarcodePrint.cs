using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Entities;

public class BarcodePrint
{
    public int Id { get; set; }

    public string BarcodeNo { get; set; } = string.Empty;

    public string SalesOrderNo { get; set; } = string.Empty;

    public string? LineNo { get; set; }

    public string? VendorCode { get; set; }

    public LabelType? LabelType { get; set; }

    public string MaterialCode { get; set; } = string.Empty;

    public string? MaterialName { get; set; }

    /// <summary>
    /// SAP SERNR (tekrar basım için). Boşsa BarcodeNo ile doldurulabilir.
    /// </summary>
    public string? SerialNumber { get; set; }

    public decimal Quantity { get; set; }

    public DateTime PrintDate { get; set; } = DateTime.UtcNow;

    public string? PrintedByUserId { get; set; }

    public ApplicationUser? PrintedByUser { get; set; }

    public BarcodePrintStatus Status { get; set; } = BarcodePrintStatus.Pending;

    public bool SapSent { get; set; }

    public DateTime? SapSentDate { get; set; }

    public ICollection<SapTransferLog> TransferLogs { get; set; } = [];
}
