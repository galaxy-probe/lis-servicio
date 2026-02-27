namespace LisServicioSecure.Models;

public sealed class SignedTicket {
    public string Kid { get; set; } = "";
    public string JobId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Action { get; set; } = "";
    public string PrinterType { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string PayloadBase64 { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
    public long Iat { get; set; }
    public long Exp { get; set; }
    public string Nonce { get; set; } = "";
    public string Sig { get; set; } = "";
}
