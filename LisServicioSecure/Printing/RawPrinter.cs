using Serilog;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LisServicioSecure.Printing;

public static class RawPrinter {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DOCINFOA {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "RAW";
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile = null;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType = "RAW";
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    /// <summary>
    /// Envía bytes RAW (ZPL/EPL/etc.) a una impresora instalada en Windows.
    /// Devuelve ok/err en vez de lanzar excepción.
    /// </summary>
    public static bool SendBytes(string printerName, byte[] bytes, out string err) {
        err = "";

        if (string.IsNullOrWhiteSpace(printerName)) {
            err = "Nombre de impresora vacío.";
            return false;
        }

        if (bytes is null || bytes.Length == 0) {
            err = "No hay datos para imprimir (bytes vacíos).";
            return false;
        }

        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pUnmanaged = IntPtr.Zero;

        try {
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                return FailWin32("OpenPrinter", out err);

            var doc = new DOCINFOA { pDocName = "ZPL RAW", pDataType = "RAW" };

            if (StartDocPrinter(hPrinter, 1, doc) == 0)
                return FailWin32("StartDocPrinter", out err);

            if (!StartPagePrinter(hPrinter))
                return FailWin32("StartPagePrinter", out err);

            pUnmanaged = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, pUnmanaged, bytes.Length);

            if (!WritePrinter(hPrinter, pUnmanaged, bytes.Length, out int written))
                return FailWin32("WritePrinter", out err);

            if (written != bytes.Length) {
                err = $"WritePrinter escribió incompleto: {written}/{bytes.Length} bytes.";
                return false;
            }

            if (!EndPagePrinter(hPrinter))
                return FailWin32("EndPagePrinter", out err);

            if (!EndDocPrinter(hPrinter))
                return FailWin32("EndDocPrinter", out err);

            return true;
        } catch (Exception ex) {
            err = ex.Message;
            return false;
        } finally {
            if (pUnmanaged != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pUnmanaged);

            if (hPrinter != IntPtr.Zero)
                ClosePrinter(hPrinter);
        }
    }

    /// <summary>
    /// Helper opcional si quieres mantener "SendZpl(printer, zpl)" también.
    /// </summary>
    public static bool SendZpl(string printerName, string zpl, out string err) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(zpl ?? "");
        return SendBytes(printerName, bytes, out err);
    }

    private static bool FailWin32(string step, out string err) {
        int code = Marshal.GetLastWin32Error();
        string msg = new Win32Exception(code).Message;
        err = $"{step} falló. Win32Error={code} ({msg})";
        return false;
    }
}
