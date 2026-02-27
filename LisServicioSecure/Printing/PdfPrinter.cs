using LisServicioSecure.Models;
using Microsoft.Extensions.Options;
using RawPrint.NetStd;
using Serilog;

namespace LisServicioSecure.Printing;

public sealed class PdfPrinter {
    private readonly IOptionsMonitor<PrintingOptions> _opt;

    public PdfPrinter(IOptionsMonitor<PrintingOptions> opt) => _opt = opt;

    public (bool ok, string msg) PrintBase64Pdf(string base64, string? tipo) {
        var printerName = (tipo?.Equals("zebra", StringComparison.OrdinalIgnoreCase) ?? false)
            ? _opt.CurrentValue.ZebraPrinter
            : _opt.CurrentValue.NormalPrinter;

        if (string.IsNullOrWhiteSpace(printerName))
            return (false, "Impresora no configurada");

        if (string.IsNullOrWhiteSpace(base64))
            return (false, "PDF vacío (base64 vacío).");

        try {
            // Soporta "data:application/pdf;base64,JVBERi0x..."
            var comma = base64.IndexOf(',');
            if (comma >= 0 && base64.Substring(0, comma).Contains("base64", StringComparison.OrdinalIgnoreCase))
                base64 = base64[(comma + 1)..];

            byte[] pdfBytes;
            try {
                pdfBytes = Convert.FromBase64String(base64);
            } catch (FormatException) {
                return (false, "Base64 inválido (no se pudo decodificar).");
            }

            if (pdfBytes.Length == 0)
                return (false, "PDF vacío (0 bytes).");
           
            var documentName = $"LisServicioSecure_{DateTime.Now:yyyyMMdd_HHmmss}";
            using var ms = new MemoryStream(pdfBytes);

            IPrinter printer = new Printer();
            printer.PrintRawStream(printerName, ms, documentName);

            Log.Information("📄 PDF impreso OK en {Printer}", printerName);
            return (true, "PDF enviado a impresora");
        } catch (Exception ex) {
            Log.Error(ex, "📄 PDF ERROR");
            return (false, ex.Message);
        }
    }
}
