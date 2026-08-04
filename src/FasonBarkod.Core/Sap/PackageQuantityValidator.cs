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
    /// Yeni koli üstü: basilacak &gt; 0, ≤ MENGE, paket katı.
    /// Yeni koli içi: basilacak ≥ 1; paket ≤ MENGE olmalı (aksi halde SAP Max:0 verir).
    /// </summary>
    public static string? ValidateNewPrint(
        LabelType labelType,
        decimal printQuantity,
        decimal packageQuantity,
        decimal orderQuantity,
        decimal originalPackageQuantity)
    {
        var packageError = ValidatePackageQuantity(packageQuantity, originalPackageQuantity);
        if (packageError is not null)
        {
            return packageError;
        }

        // Koli üstü: paket > MENGE olamaz (kat/etiket hesabı bozulur).
        // Koli içi: formda SAP paketi büyük kalsa bile SAP'ye min(paket, basılacak) gider.
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

            if (orderQuantity > 0 && printQuantity > orderQuantity)
            {
                return $"Basılacak miktar sipariş miktarını ({orderQuantity:0.####}) aşamaz.";
            }

            if (printQuantity % packageQuantity != 0)
            {
                return NotMultipleError;
            }

            return null;
        }

        if (labelType == LabelType.KoliIci)
        {
            if (printQuantity < 1)
            {
                return printQuantity <= 0 ? BoxInsideZeroError : BoxInsideEmptyError;
            }

            if (orderQuantity > 0 && printQuantity > orderQuantity)
            {
                return $"Koli içi adet sipariş miktarını ({orderQuantity:0.####}) aşamaz.";
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
    /// SAP koli içi kotası tahmini: MENGE − (basılan koli × paket) − basılan koli içi.
    /// Paket &gt; MENGE ise 0 (önce paket düşürülmeli; aksi halde SAP Max:0).
    /// MENGE bilinmiyorsa null (kısıtlama yok).
    /// </summary>
    public static decimal? RemainingInsideQuantity(
        decimal orderQuantity,
        decimal packageQuantity,
        int printedBoxCount,
        int printedInsideBoxCount)
    {
        if (orderQuantity <= 0)
        {
            return null;
        }

        if (packageQuantity > orderQuantity)
        {
            return 0;
        }

        var pkg = packageQuantity > 0 ? packageQuantity : 0;
        var remaining = orderQuantity - (printedBoxCount * pkg) - printedInsideBoxCount;
        return remaining < 0 ? 0 : remaining;
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
