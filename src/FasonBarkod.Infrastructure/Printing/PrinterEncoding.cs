using System.Text;

namespace FasonBarkod.Infrastructure.Printing;

/// <summary>
/// SATO CL408e gibi etiket yazıcıları için karakter dönüşümü. Yazıcı/kod sayfası uyumluluğu
/// garanti olmadığından Türkçe özel karakterler en yakın ASCII karşılığına çevrilir
/// (ı→i, ş→s, ğ→g, ü→u, ö→o, ç→c) — böylece printer'ın aktif kod sayfası ne olursa olsun
/// karakterler doğru ve öngörülebilir basılır.
/// </summary>
public static class PrinterEncoding
{
    private static readonly object Sync = new();
    private static bool _providerRegistered;

    private static readonly Dictionary<char, char> TurkishToAscii = new()
    {
        ['ı'] = 'i', ['İ'] = 'I',
        ['ş'] = 's', ['Ş'] = 'S',
        ['ğ'] = 'g', ['Ğ'] = 'G',
        ['ü'] = 'u', ['Ü'] = 'U',
        ['ö'] = 'o', ['Ö'] = 'O',
        ['ç'] = 'c', ['Ç'] = 'C',
    };

    public static Encoding Turkish => GetWindows1254();

    /// <summary>
    /// Türkçe özel karakterleri en yakın ASCII karşılığına çevirir (ı→i, ş→s vb.).
    /// SATO yazıcılarda kod sayfası uyumsuzluğundan kaynaklanan bozuk karakter riskini ortadan kaldırır.
    /// </summary>
    public static string ToAsciiSafe(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            chars[i] = TurkishToAscii.TryGetValue(text[i], out var replacement) ? replacement : text[i];
        }

        return new string(chars);
    }

    /// <summary>
    /// Yazıcıya gönderilecek ham baytları üretir: önce Türkçe karakterler ASCII'ye çevrilir,
    /// sonra düz ASCII olarak encode edilir (kod sayfası bağımlılığı kalmaz).
    /// </summary>
    public static byte[] GetBytes(string text) => Encoding.ASCII.GetBytes(ToAsciiSafe(text));

    private static Encoding GetWindows1254()
    {
        EnsureProviderRegistered();
        return Encoding.GetEncoding(1254);
    }

    private static void EnsureProviderRegistered()
    {
        if (_providerRegistered)
        {
            return;
        }

        lock (Sync)
        {
            if (_providerRegistered)
            {
                return;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _providerRegistered = true;
        }
    }
}
