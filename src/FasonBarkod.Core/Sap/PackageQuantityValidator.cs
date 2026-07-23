using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Sap;

public static class PackageQuantityValidator
{
    public const string NotMultipleError = "Girilen Değer, Paket Miktarının Katı Olmalıdır.!";

    /// <summary>
    /// Koli İçi etiketi, kolinin içindeki her bir bağımsız ürünü temsil eder; bu nedenle basım
    /// adedi paket miktarına eşit olmalıdır (paket miktarı 10 ise koli içi basım adedi de 10 olmalı).
    /// Katı olması yetmez — örn. paket miktarı 1 iken 5 girilmesi geçersizdir.
    /// </summary>
    public const string NotEqualToPackageError = "Koli İçi Basım Adedi, Paket Miktarına Eşit Olmalıdır.!";

    public static bool IsValidMultiple(decimal printQuantity, decimal packageQuantity, LabelType labelType)
    {
        if (packageQuantity <= 0 || printQuantity <= 0)
        {
            return false;
        }

        return labelType switch
        {
            LabelType.KoliUstu => printQuantity % packageQuantity == 0,
            LabelType.KoliIci => printQuantity == packageQuantity,
            _ => false
        };
    }

    public static string GetValidationError(LabelType labelType) =>
        labelType == LabelType.KoliIci ? NotEqualToPackageError : NotMultipleError;

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
