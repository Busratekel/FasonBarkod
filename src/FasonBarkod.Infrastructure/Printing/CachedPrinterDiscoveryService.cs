using Microsoft.Extensions.Caching.Memory;

namespace FasonBarkod.Infrastructure.Printing;

/// <summary>
/// Yazıcı listesi Windows'ta yavaş olabildiği için kısa süreli önbellek kullanır.
/// </summary>
public class CachedPrinterDiscoveryService(
    WindowsPrinterDiscoveryService inner,
    IMemoryCache cache) : IPrinterDiscoveryService
{
    private const string InstalledKey = "printer-discovery:installed";
    private const string DefaultKey = "printer-discovery:default";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    public IReadOnlyList<string> GetInstalledPrinters() =>
        cache.GetOrCreate(InstalledKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return inner.GetInstalledPrinters();
        }) ?? [];

    public string? GetDefaultPrinterName() =>
        cache.GetOrCreate(DefaultKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return inner.GetDefaultPrinterName();
        });

    public string ResolvePrinterName(string? preferredPrinterName) =>
        inner.ResolvePrinterName(preferredPrinterName);
}
