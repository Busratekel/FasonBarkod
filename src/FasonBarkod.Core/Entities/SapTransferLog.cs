using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Entities;

public class SapTransferLog
{
    public int Id { get; set; }

    public int BarcodePrintId { get; set; }

    public BarcodePrint BarcodePrint { get; set; } = null!;

    public DateTime TransferDate { get; set; } = DateTime.UtcNow;

    public SapTransferResult Result { get; set; } = SapTransferResult.Pending;

    public string? ErrorMessage { get; set; }

    public string? RequestPayload { get; set; }

    public string? ResponsePayload { get; set; }
}
