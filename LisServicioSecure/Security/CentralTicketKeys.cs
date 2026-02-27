using Microsoft.Extensions.Configuration;

namespace LisServicioSecure.Security;

public sealed class CentralTicketKeys {
    private readonly Dictionary<string, string> _keys;

    public CentralTicketKeys(IConfiguration cfg) {
        // appsettings.json:
        // "CentralTicket": { "Keys": { "2026-02-01": "...", "2026-01-01": "..." } }
        _keys = cfg.GetSection("CentralTicket:Keys").Get<Dictionary<string, string>>()
               ?? new Dictionary<string, string>();

        if (_keys.Count == 0)
            throw new InvalidOperationException("CentralTicket:Keys no configurado");
    }

    public bool TryGetKey(string kid, out string secret)
        => _keys.TryGetValue(kid, out secret!);
}
