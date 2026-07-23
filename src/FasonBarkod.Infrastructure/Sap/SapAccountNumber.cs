namespace FasonBarkod.Infrastructure.Sap;

internal static class SapAccountNumber
{
    /// <summary>
    /// SAP CHAR10 hesap numaraları (EBELN, LIFNR, KUNNR) — başına sıfır ekler.
    /// </summary>
    public static string Pad10(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = value.Trim();
        return digits.Length >= 10 ? digits[^10..] : digits.PadLeft(10, '0');
    }

    public static string? Pad10OrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Pad10(value);

    /// <summary>
    /// SAP CHAR alanları için boş değer — 10 boşluk.
    /// </summary>
    public static string Char10Spaces() => new(' ', 10);
}
