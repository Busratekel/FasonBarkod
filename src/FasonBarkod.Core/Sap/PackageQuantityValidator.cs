using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Sap;

/// <summary>
/// SAS etiket kuralları — frmGetBarcodesSas / GetSAPSASBarcode* ile uyumlu.
/// </summary>
public static class PackageQuantityValidator
{
    public const string NotMultipleError = "Girilen Değer, Paket Miktarının Katı Olmalıdır.!";
    public const string PackageTooLargeError = "Girilen Değer, Paket Miktarıdan Büyük Olamaz.";
    public const string PackageRequiredError = "Lütfen PaKet Miktarını Kontrol Ediniz.";
    public const string BoxTopEmptyError = "Basılacak Koli Üstü Miktar boş olamaz.";
    public const string BoxInsideEmptyError = "Boş Olamaz";
    public const string BoxInsideZeroError = "Sıfır Olamaz";
    public const string PackageExceedsOrderError =
        "Paket miktarı sipariş miktarından büyük. Önce paketi sipariş miktarına (veya daha aza) düşürün.";
    public const string BoxTopQuotaExhaustedError =
        "Koli üstü basılamaz (sipariş kotası dolmuş).";

    /// <summary>
    /// Paket: &gt; 0 ve SAP satırındaki orijinal PAKET_MIKTARI’ndan büyük olamaz.
    /// </summary>
    public static string? ValidatePackageQuantity(decimal packageQuantity, decimal originalPackageQuantity)
    {
        if (packageQuantity <= 0)
        {
            return PackageRequiredError;
        }

        if (originalPackageQuantity > 0 && packageQuantity > originalPackageQuantity)
        {
            return PackageTooLargeError;
        }

        return null;
    }

    /// <summary>
    /// Koli üstü kalan miktar: MENGE − (basılan koli × paket).
    /// MENGE bilinmiyorsa null.
    /// </summary>
    public static decimal? RemainingBoxTopQuantity(
        decimal orderQuantity,
        decimal packageQuantity,
        int printedBoxCount)
    {
        if (orderQuantity <= 0)
        {
            return null;
        }

        var pkg = packageQuantity > 0 ? packageQuantity : 0;
        var remaining = orderQuantity - (printedBoxCount * pkg);
        return remaining < 0 ? 0 : remaining;
    }

    /// <summary>
    /// Yeni koli üstü: basilacak &gt; 0, paket katı, ≤ kalan kota (MENGE − basılan×paket).
    /// Yeni koli içi (Doqu): basilacak ≥ 1; MENGE tavanı / paket katı yok — kota SAP'de.
    /// </summary>
    public static string? ValidateNewPrint(
        LabelType labelType,
        decimal printQuantity,
        decimal packageQuantity,
        decimal orderQuantity,
        decimal originalPackageQuantity,
        int printedBoxCount = 0)
    {
        var packageError = ValidatePackageQuantity(packageQuantity, originalPackageQuantity);
        if (packageError is not null)
        {
            return packageError;
        }

        // Koli üstü: paket > MENGE olamaz (kat/etiket hesabı bozulur).
        if (labelType == LabelType.KoliUstu
            && orderQuantity > 0
            && packageQuantity > orderQuantity)
        {
            return $"{PackageExceedsOrderError} (paket={packageQuantity:0.####}, sipariş={orderQuantity:0.####}).";
        }

        if (labelType == LabelType.KoliUstu)
        {
            if (printQuantity <= 0)
            {
                return BoxTopEmptyError;
            }

            if (printQuantity % packageQuantity != 0)
            {
                return NotMultipleError;
            }

            var remaining = RemainingBoxTopQuantity(orderQuantity, packageQuantity, printedBoxCount);
            if (remaining is 0)
            {
                return BoxTopQuotaExhaustedError
                    + $" (sipariş={orderQuantity:0.####}, basılan koli={printedBoxCount}, paket={packageQuantity:0.####}).";
            }

            if (remaining is > 0 && printQuantity > remaining.Value)
            {
                return $"Koli üstü en fazla {remaining.Value:0.####} basılabilir (kalan kota)."
                    + $" Sipariş {orderQuantity:0.####}, basılan koli {printedBoxCount}.";
            }

            if (orderQuantity > 0 && printQuantity > orderQuantity)
            {
                return $"Basılacak miktar sipariş miktarını ({orderQuantity:0.####}) aşamaz.";
            }

            return null;
        }

        if (labelType == LabelType.KoliIci)
        {
            if (printQuantity < 1)
            {
                return printQuantity <= 0 ? BoxInsideZeroError : BoxInsideEmptyError;
            }

            return null;
        }

        return "Geçersiz etiket tipi.";
    }

    /// <summary>Koli üstü etiket adedi = basilacak / paket. Koli içi = basilacak.</summary>
    public static int CalculateLabelCount(decimal printQuantity, decimal packageQuantity, LabelType labelType)
    {
        return labelType switch
        {
            LabelType.KoliUstu when packageQuantity > 0 => (int)(printQuantity / packageQuantity),
            LabelType.KoliIci => (int)printQuantity,
            _ => 0
        };
    }

    /// <summary>
    /// Yerel koli içi kota hesabı yok (Doqu: MENGE tavanı yok; kota SAP'de).
    /// </summary>
    public static decimal? RemainingInsideQuantity(
        decimal orderQuantity,
        decimal packageQuantity,
        int printedBoxCount,
        int printedInsideBoxCount)
    {
        _ = orderQuantity;
        _ = packageQuantity;
        _ = printedBoxCount;
        _ = printedInsideBoxCount;
        return null;
    }

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

    public static string GetValidationError(LabelType labelType) =>
        labelType == LabelType.KoliIci ? BoxInsideEmptyError : NotMultipleError;
}
