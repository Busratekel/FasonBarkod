namespace FasonBarkod.Core.Enums;

public enum BarcodePrintStatus
{
    Pending = 0,
    Printed = 1,
    SentToSap = 2,
    Failed = 3,
    Cancelled = 4
}
