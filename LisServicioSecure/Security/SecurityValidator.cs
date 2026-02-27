//using Microsoft.Extensions.Configuration;
//using System.Collections.Concurrent;
//using System.Security.Cryptography;
//using System.Text;

//namespace LisServicioSecure.Security;

//public sealed class SecurityValidator {
//    private readonly string _token;
//    private readonly byte[] _hmacKey;
//    private readonly int _maxSkewSeconds;

//    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenNonces = new();

//    public SecurityValidator(IConfiguration cfg) {
//        _token = cfg["Security:Token"] ?? "";
//        _hmacKey = Encoding.UTF8.GetBytes(cfg["Security:HmacSecret"] ?? "");
//        _maxSkewSeconds = cfg.GetValue("Security:MaxSkewSeconds", 120);
//    }

//    public bool Validate(string token, long ts, string nonce, string sig, out string error) {
//        error = "";

//        if (string.IsNullOrWhiteSpace(_token) || token != _token) {
//            error = "Token inválido";
//            return false;
//        }

//        if (_hmacKey.Length < 16) {
//            error = "HmacSecret mal configurado (muy corto)";
//            return false;
//        }

//        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
//        if (Math.Abs(now - ts) > _maxSkewSeconds) {
//            error = "Timestamp fuera de ventana";
//            return false;
//        }

//        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length < 8) {
//            error = "Nonce inválido";
//            return false;
//        }

//        CleanupOldNonces();

//        // anti-replay
//        if (!_seenNonces.TryAdd($"{ts}:{nonce}", DateTimeOffset.UtcNow)) {
//            error = "Nonce repetido (replay)";
//            return false;
//        }

//        var expected = ComputeSig(token, ts, nonce);
//        if (!CryptographicOperations.FixedTimeEquals(
//                Encoding.UTF8.GetBytes(expected),
//                Encoding.UTF8.GetBytes(sig ?? ""))) {
//            error = "Firma inválida";
//            return false;
//        }

//        return true;
//    }

//    private string ComputeSig(string token, long ts, string nonce) {
//        var msg = $"{token}|{ts}|{nonce}";
//        using var h = new HMACSHA256(_hmacKey);
//        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(msg));
//        return Convert.ToHexString(hash).ToLowerInvariant();
//    }

//    private void CleanupOldNonces() {
//        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_maxSkewSeconds * 2);
//        foreach (var kv in _seenNonces) {
//            if (kv.Value < cutoff)
//                _seenNonces.TryRemove(kv.Key, out _);
//        }
//    }
//}
