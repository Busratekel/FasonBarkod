namespace FasonBarkod.Infrastructure.Sap;

public static class SapAccountNumber
{
    /// <summary>
    /// SAP CHAR10 hesap numaraları (EBELN, LIFNR, KUNNR) — başına sıfır ekler.
    /// 10 haneden uzunsa kırpmaz (yanlış EBELN üretir); olduğu gibi bırakır.
    /// </summary>
    public static string Pad10(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = new string(value.Trim().Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return value.Trim();
        }

        // Eskiden [^10..] ile kırpılıyordu → 51000004270 → 1000004270 (yanlış).
        if (digits.Length > 10)
        {
            return digits;
        }

        return digits.PadLeft(10, '0');
    }

    public static string? Pad10OrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Pad10(value);

    /// <summary>SAS (EBELN) için geçerli mi? SAP alanı CHAR10.</summary>
    public static bool TryNormalizeEbeln(string? value, out string ebeln, out string? error)
    {
        ebeln = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "SAS numarası boş olamaz.";
            return false;
        }

        var digits = new string(value.Trim().Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            error = "SAS numarası yalnızca rakam olmalıdır.";
            return false;
        }

        if (digits.Length > 10)
        {
            error =
                $"SAS numarası 10 haneli olmalı (girdiğiniz: {digits.Length} hane). " +
                "Numarayı kontrol edin; fazla sıfır veya yanlış haneler olabilir.";
            return false;
        }

        ebeln = digits.PadLeft(10, '0');
        return true;
    }

    /// <summary>
    /// SAP CHAR alanları için boş değer — 10 boşluk.
    /// </summary>
    public static string Char10Spaces() => new(' ', 10);
}
