namespace LisServicioSecure.Models;

public sealed class PrintingOptions {
    public string NormalPrinter { get; set; } = "";
    public string ZebraPrinter { get; set; } = "";
    public int MaxPayloadBytes { get; set; } = 5_242_880; // 5MB
}
