using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Sap;

public static class PackageQuantityValidator
{
    public const string NotMultipleError = "Girilen Değer, Paket Miktarının Katı Olmalıdır.!";

    public static bool IsValidMultiple(decimal printQuantity, decimal packageQuantity, LabelType labelType)
    {
        if (packageQuantity <= 0 || printQuantity <= 0)
        {
            return false;
        }

        return labelType switch
        {
            LabelType.KoliUstu => printQuantity % packageQuantity == 0,
            LabelType.KoliIci => true,
            _ => false
        };
    }

    public static int CalculateLabelCount(decimal printQuantity, decimal packageQuantity, LabelType labelType)
    {
        return labelType switch
        {
            LabelType.KoliUstu => (int)(printQuantity / packageQuantity),
            LabelType.KoliIci => (int)printQuantity,
            _ => 0
        };
    }
}
