using Microsoft.Extensions.Caching.Memory;

namespace FasonBarkod.Infrastructure.Printing;

/// <summary>
/// Yazıcı listesi Windows'ta yavaş olabildiği için kısa süreli önbellek kullanır.
/// Boş sonuç önbelleğe alınmaz (IIS ilk istekte kimlik/izin gecikmesi olabilir).
/// </summary>
public class CachedPrinterDiscoveryService(
    WindowsPrinterDiscoveryService inner,
    IMemoryCache cache) : IPrinterDiscoveryService
{
    private const string InstalledKey = "printer-discovery:installed";
    private const string DefaultKey = "printer-discovery:default";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        if (cache.TryGetValue(InstalledKey, out IReadOnlyList<string>? cached) && cached is { Count: > 0 })
        {
            return cached;
        }

        var printers = inner.GetInstalledPrinters();
        if (printers.Count > 0)
        {
            cache.Set(InstalledKey, printers, CacheDuration);
        }

        return printers;
    }

    public string? GetDefaultPrinterName()
    {
        if (cache.TryGetValue(DefaultKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var name = inner.GetDefaultPrinterName();
        if (!string.IsNullOrWhiteSpace(name))
        {
            cache.Set(DefaultKey, name, CacheDuration);
        }

        return name;
    }

    public string ResolvePrinterName(string? preferredPrinterName) =>
        inner.ResolvePrinterName(preferredPrinterName);
}
