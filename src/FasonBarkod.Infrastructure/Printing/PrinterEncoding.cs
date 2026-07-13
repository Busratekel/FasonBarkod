using System.Text;

namespace FasonBarkod.Infrastructure.Printing;

/// <summary>
/// SATO yazıcılar Türkçe karakterler için Windows-1254 kullanır.
/// .NET Core+ bu kod sayfasını varsayılan olarak içermez; CodePages provider gerekir.
/// </summary>
public static class PrinterEncoding
{
    private static readonly object Sync = new();
    private static bool _providerRegistered;

    public static Encoding Turkish => GetWindows1254();

    public static byte[] GetBytes(string text) => Turkish.GetBytes(text);

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
