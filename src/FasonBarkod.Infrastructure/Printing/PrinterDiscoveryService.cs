using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace FasonBarkod.Infrastructure.Printing;

public interface IPrinterDiscoveryService
{
    IReadOnlyList<string> GetInstalledPrinters();

    string? GetDefaultPrinterName();

    string ResolvePrinterName(string? preferredPrinterName);
}

[SupportedOSPlatform("windows")]
public class WindowsPrinterDiscoveryService(ILogger<WindowsPrinterDiscoveryService>? logger = null) : IPrinterDiscoveryService
{
    private const int PrinterEnumLocal = 0x00000002;
    private const int PrinterEnumConnections = 0x00000010;
    private const int PrinterEnumFlags = PrinterEnumLocal | PrinterEnumConnections;
    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(8);

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return [];
        }

        try
        {
            var task = Task.Run(CollectInstalledPrinters);
            if (!task.Wait(EnumerationTimeout))
            {
                logger?.LogWarning("Yazıcı listesi {Seconds}s içinde alınamadı.", EnumerationTimeout.TotalSeconds);
                return CollectFromRegistry();
            }

            return task.Result;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Yazıcı listesi alınırken hata oluştu");
            return CollectFromRegistry();
        }
    }

    private IReadOnlyList<string> CollectInstalledPrinters()
    {
        var printers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Makineye kurulu yazıcılar — IIS App Pool altında en güvenilir kaynak.
        foreach (var name in CollectFromRegistry())
        {
            printers.Add(name);
        }

        // 2) System.Drawing — klasik .NET listesi.
        try
        {
            foreach (string name in PrinterSettings.InstalledPrinters)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    printers.Add(name.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "PrinterSettings.InstalledPrinters başarısız");
        }

        // 3) EnumPrinters level 4 (doğru IntPtr marshaling ile).
        CollectPrintersLevel4(printers);

        var defaultPrinter = GetDefaultPrinterName();
        if (!string.IsNullOrWhiteSpace(defaultPrinter))
        {
            printers.Add(defaultPrinter);
        }

        if (printers.Count == 0)
        {
            logger?.LogWarning("Windows yazıcı listesi boş. IIS App Pool kimliğini LocalSystem veya yazıcıyı kuran kullanıcı yapın.");
        }
        else
        {
            logger?.LogDebug("Yazıcı listesi: {Count}", printers.Count);
        }

        return printers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Makineye kurulu yazıcılar (HKLM) + oturum açmış kullanıcıların ağ yazıcı bağlantıları (HKU).
    /// IIS App Pool LocalSystem iken ağ yazıcıları da bulunabilir.
    /// </summary>
    private IReadOnlyList<string> CollectFromRegistry()
    {
        var printers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
            if (key is not null)
            {
                foreach (var name in key.GetSubKeyNames())
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        printers.Add(name.Trim());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "HKLM yazıcı listesi okunamadı");
        }

        // Ağ yazıcıları (\\server\printer) kullanıcı profilinde tutulur.
        try
        {
            using var users = Registry.Users;
            foreach (var sid in users.GetSubKeyNames())
            {
                // S-1-5-21-... kullanıcı SID'leri; .bak ve sistem hesaplarını atla
                if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var connections = users.OpenSubKey($@"{sid}\Printers\Connections");
                    if (connections is null)
                    {
                        continue;
                    }

                    foreach (var connectionName in connections.GetSubKeyNames())
                    {
                        // Registry: ,,10.100.3.158,BY6_SATO_CL4NX_YAZILIM  →  \\10.100.3.158\BY6_SATO_CL4NX_YAZILIM
                        var unc = NormalizePrinterConnectionName(connectionName);
                        if (!string.IsNullOrWhiteSpace(unc))
                        {
                            printers.Add(unc);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "HKU {Sid} yazıcı bağlantıları okunamadı", sid);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "HKU yazıcı taraması başarısız");
        }

        return printers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? NormalizePrinterConnectionName(string? registryName)
    {
        if (string.IsNullOrWhiteSpace(registryName))
        {
            return null;
        }

        // ",,server,printer" veya "\\server\printer"
        var name = registryName.Trim();
        if (name.StartsWith(",,", StringComparison.Ordinal))
        {
            var parts = name.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $@"\\{parts[0]}\{parts[1]}";
            }
        }

        if (name.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return name;
        }

        return name.Replace(',', '\\').Trim('\\') is { Length: > 0 } converted
            ? $@"\\{converted}"
            : null;
    }

    private void CollectPrintersLevel4(HashSet<string> printers)
    {
        EnumPrinters(PrinterEnumFlags, null, 4, IntPtr.Zero, 0, out var needed, out _);
        if (needed <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumPrinters(PrinterEnumFlags, null, 4, buffer, needed, out _, out var count) || count <= 0)
            {
                logger?.LogDebug("EnumPrinters level 4 başarısız (Win32: {Error})", Marshal.GetLastWin32Error());
                return;
            }

            var structSize = Marshal.SizeOf<PRINTER_INFO_4>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PRINTER_INFO_4>(buffer + (i * structSize));
                var name = Marshal.PtrToStringAuto(info.pPrinterName);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    printers.Add(name.Trim());
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public string? GetDefaultPrinterName()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            var settings = new PrinterSettings();
            if (!string.IsNullOrWhiteSpace(settings.PrinterName))
            {
                return settings.PrinterName;
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "PrinterSettings.PrinterName okunamadı");
        }

        var size = 0;
        GetDefaultPrinter(null!, ref size);
        if (size <= 0)
        {
            return null;
        }

        var buffer = new StringBuilder(size + 1);
        if (!GetDefaultPrinter(buffer, ref size))
        {
            return null;
        }

        return buffer.ToString();
    }

    public string ResolvePrinterName(string? preferredPrinterName)
    {
        if (!string.IsNullOrWhiteSpace(preferredPrinterName))
        {
            return preferredPrinterName.Trim();
        }

        var defaultPrinter = GetDefaultPrinterName();
        if (!string.IsNullOrWhiteSpace(defaultPrinter))
        {
            return defaultPrinter;
        }

        var installed = GetInstalledPrinters();
        if (installed.Count > 0)
        {
            return installed[0];
        }

        throw new InvalidOperationException(
            "Bu bilgisayarda yazıcı bulunamadı. IIS App Pool kimliğini LocalSystem veya yazıcıyı kuran Windows kullanıcısı yapın.");
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(
        int flags,
        string? name,
        int level,
        IntPtr buffer,
        int cbBuf,
        out int pcbNeeded,
        out int pcReturned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder pszBuffer, ref int pcchBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_4
    {
        public IntPtr pPrinterName;
        public IntPtr pServerName;
        public uint Attributes;
    }
}

public class UnsupportedPrinterDiscoveryService : IPrinterDiscoveryService
{
    public IReadOnlyList<string> GetInstalledPrinters() => [];

    public string? GetDefaultPrinterName() => null;

    public string ResolvePrinterName(string? preferredPrinterName)
    {
        if (!string.IsNullOrWhiteSpace(preferredPrinterName))
        {
            return preferredPrinterName.Trim();
        }

        throw new PlatformNotSupportedException("Yazıcı keşfi yalnızca Windows'ta desteklenir.");
    }
}
