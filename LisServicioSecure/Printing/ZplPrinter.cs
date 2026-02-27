using LisServicioSecure.Models;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text;

namespace LisServicioSecure.Printing;

public sealed class ZplPrinter {
    private readonly IOptionsMonitor<PrintingOptions> _opt;

    public ZplPrinter(IOptionsMonitor<PrintingOptions> opt) => _opt = opt;

    public (bool ok, string msg) PrintBase64Zpl(string base64) {
        var printer = _opt.CurrentValue.ZebraPrinter;
        if (string.IsNullOrWhiteSpace(printer))
            return (false, "ZebraPrinter no configurada");

        var zpl = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var bytes = Encoding.UTF8.GetBytes(zpl);

//        RawPrinterHelper.SendZpl(printer , zpl);

        var ok = RawPrinter.SendBytes(printer, bytes, out var err);
        if (ok) Log.Information("🦓 ZPL impreso OK en {Printer}", printer);
        else Log.Error("🦓 ZPL ERROR en {Printer}: {Err}", printer, err);

        return ok ? (true, "ZPL enviado a impresora") : (false, err);

    }
}
