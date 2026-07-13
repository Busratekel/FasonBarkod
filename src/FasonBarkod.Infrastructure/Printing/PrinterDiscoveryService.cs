using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

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
    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(5);

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
                logger?.LogWarning("Yazıcı listesi {Seconds}s içinde alınamadı; varsayılan yazıcı kullanılacak.", EnumerationTimeout.TotalSeconds);
                return FallbackPrinterList();
            }

            return task.Result;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Yazıcı listesi alınırken hata oluştu");
            return FallbackPrinterList();
        }
    }

    private IReadOnlyList<string> CollectInstalledPrinters()
    {
        var printers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectPrinters(printers, level: 2);
        if (printers.Count == 0)
        {
            CollectPrinters(printers, level: 1);
        }

        var defaultPrinter = GetDefaultPrinterName();
        if (!string.IsNullOrWhiteSpace(defaultPrinter))
        {
            printers.Add(defaultPrinter);
        }

        if (printers.Count == 0)
        {
            logger?.LogWarning("Windows yazıcı listesi boş döndü (EnumPrinters).");
        }

        return printers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<string> FallbackPrinterList()
    {
        var defaultPrinter = GetDefaultPrinterName();
        return string.IsNullOrWhiteSpace(defaultPrinter) ? [] : [defaultPrinter];
    }

    private void CollectPrinters(HashSet<string> printers, int level)
    {
        EnumPrinters(PrinterEnumFlags, null, level, IntPtr.Zero, 0, out var needed, out _);
        if (needed <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumPrinters(PrinterEnumFlags, null, level, buffer, needed, out _, out var count))
            {
                logger?.LogDebug("EnumPrinters level {Level} başarısız (Win32: {Error})", level, Marshal.GetLastWin32Error());
                return;
            }

            var structSize = level switch
            {
                1 => Marshal.SizeOf<PRINTER_INFO_1>(),
                2 => Marshal.SizeOf<PRINTER_INFO_2>(),
                4 => Marshal.SizeOf<PRINTER_INFO_4>(),
                _ => 0
            };

            if (structSize <= 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var name = level switch
                {
                    1 => Marshal.PtrToStructure<PRINTER_INFO_1>(buffer + (i * structSize)).pName,
                    2 => Marshal.PtrToStructure<PRINTER_INFO_2>(buffer + (i * structSize)).pPrinterName,
                    4 => Marshal.PtrToStructure<PRINTER_INFO_4>(buffer + (i * structSize)).pPrinterName,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(name))
                {
                    printers.Add(name);
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

        throw new InvalidOperationException("Bu bilgisayarda yazıcı bulunamadı. Windows yazıcı ayarlarını kontrol edin.");
    }

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool EnumPrinters(
        int flags,
        string? name,
        int level,
        IntPtr buffer,
        int cbBuf,
        out int pcbNeeded,
        out int pcReturned);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder pszBuffer, ref int pcchBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PRINTER_INFO_1
    {
        public string pName;
        public string pDescription;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PRINTER_INFO_2
    {
        public string pServerName;
        public string pPrinterName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PRINTER_INFO_4
    {
        public string pPrinterName;
        public string? pServerName;
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
