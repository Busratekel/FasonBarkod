using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FasonBarkod.Infrastructure.Printing;

[SupportedOSPlatform("windows")]
internal static class WindowsRawPrinter
{
    public static void Send(string printerName, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new InvalidOperationException("Yazıcı adı tanımlı değil.");
        }

        if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
        {
            throw new InvalidOperationException($"Yazıcı açılamadı: {printerName} (Win32: {Marshal.GetLastWin32Error()})");
        }

        try
        {
            var docInfo = new DOC_INFO_1
            {
                pDocName = "FasonBarkod Label",
                pDataType = "RAW"
            };

            if (!StartDocPrinter(printerHandle, 1, docInfo))
            {
                throw new InvalidOperationException($"StartDocPrinter başarısız (Win32: {Marshal.GetLastWin32Error()})");
            }

            try
            {
                if (!StartPagePrinter(printerHandle))
                {
                    throw new InvalidOperationException($"StartPagePrinter başarısız (Win32: {Marshal.GetLastWin32Error()})");
                }

                try
                {
                    if (!WritePrinter(printerHandle, data, data.Length, out _))
                    {
                        throw new InvalidOperationException($"WritePrinter başarısız (Win32: {Marshal.GetLastWin32Error()})");
                    }
                }
                finally
                {
                    EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                EndDocPrinter(printerHandle);
            }
        }
        finally
        {
            ClosePrinter(printerHandle);
        }
    }

    public static byte[] ToPrinterEncoding(string rawData) =>
        PrinterEncoding.GetBytes(rawData);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(
        IntPtr hPrinter,
        byte[] pBytes,
        int dwCount,
        out int dwWritten);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DOC_INFO_1
    {
        public string pDocName = string.Empty;
        public string? pOutputFile;
        public string pDataType = "RAW";
    }
}
