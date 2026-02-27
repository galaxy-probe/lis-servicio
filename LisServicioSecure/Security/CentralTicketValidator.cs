using LisServicioSecure.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace LisServicioSecure.Security {
    /// <summary>
    /// Valida tickets firmados emitidos por la API central.
    /// - Ventana de tiempo (iat/exp) con tolerancia
    /// - Anti-replay por nonce
    /// - Validación de payload (base64 + sha256) cuando corresponde
    /// - Validación de firma HMAC-SHA256 sobre canonical string
    /// </summary>
    public sealed class CentralTicketValidator {
        private readonly CentralTicketKeys _keys;
        private readonly int _maxSkewSeconds;

        // anti-replay: nonceKey -> firstSeenUtc
        private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();

        public CentralTicketValidator(CentralTicketKeys keys, IConfiguration cfg) {
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));

            _maxSkewSeconds = cfg?.GetValue("CentralTicket:MaxSkewSeconds", 120) ?? 120;
            if (_maxSkewSeconds < 10) _maxSkewSeconds = 10;
        }

        /// <summary>
        /// Valida un ticket. Devuelve payloadBytes (vacío para getmac) si es válido.
        /// </summary>
        public bool Validate(
            SignedTicket ticket,
            out byte[] payloadBytes,
            out string error) {
            payloadBytes = Array.Empty<byte>();
            error = string.Empty;

            if (ticket is null) {
                error = "Ticket nulo";
                return false;
            }

            // Normalizaciones consistentes para validaciones lógicas y canonical
            var kid = (ticket.Kid ?? "").Trim();
            var jobId = (ticket.JobId ?? "").Trim();
            var clientId = (ticket.ClientId ?? "").Trim();
            var action = (ticket.Action ?? "").Trim().ToLowerInvariant();
            var printerType = (ticket.PrinterType ?? "").Trim().ToLowerInvariant();
            var nonce = (ticket.Nonce ?? "").Trim();
            var sig = (ticket.Sig ?? "").Trim();
            var payloadSha256 = (ticket.PayloadSha256 ?? "").Trim().ToLowerInvariant();
            var payloadBase64 = ticket.PayloadBase64 ?? "";

            // Campos mínimos
            if (string.IsNullOrWhiteSpace(kid)) {
                error = "kid requerido";
                return false;
            }

            if (string.IsNullOrWhiteSpace(jobId) ||
                string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(action) ||
                string.IsNullOrWhiteSpace(nonce) ||
                string.IsNullOrWhiteSpace(sig) ||
                string.IsNullOrWhiteSpace(payloadSha256)) {
                error = "Campos obligatorios faltantes";
                return false;
            }

            if (!_keys.TryGetKey(kid, out var secret) || string.IsNullOrWhiteSpace(secret)) {
                error = $"kid desconocido: {kid}";
                return false;
            }

            var noPayload = action == "getmac" || action == "print";

            // Payload requerido para acciones con contenido
            if (!noPayload && string.IsNullOrWhiteSpace(payloadBase64)) {
                error = "payloadBase64 requerido";
                return false;
            }

            // 1) Validar ventana de tiempo
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // iat no muy en el futuro
            if (ticket.Iat > now + _maxSkewSeconds) {
                error = "iat en el futuro";
                return false;
            }

            // exp vencido (con tolerancia)
            if (ticket.Exp < now - _maxSkewSeconds) {
                error = "ticket expirado";
                return false;
            }

            // ttl máximo defensivo
            if (ticket.Exp - ticket.Iat > 10 * 60) // 10 min máx (ajustable)
            {
                error = "ttl excesivo";
                return false;
            }

            CleanupOldNonces();

            // 2) Anti-replay por nonce (incluye clientId + jobId para evitar colisiones)
            var nonceKey = $"{clientId}:{jobId}:{nonce}";
            if (!_seen.TryAdd(nonceKey, DateTimeOffset.UtcNow)) {
                error = "nonce repetido (replay)";
                return false;
            }

            // 3) Validación de payload + hash
            if (noPayload) {
                // Para getmac: sin payload, marcador fijo "-"
                if (payloadSha256 != "-" || !string.IsNullOrWhiteSpace(payloadBase64)) {
                    error = "Ticket getmac inválido (payload no permitido)";
                    return false;
                }

                payloadBytes = Array.Empty<byte>();
            } else {
                // Decodificar base64
                try {
                    payloadBytes = Convert.FromBase64String(payloadBase64);
                } catch {
                    error = "payloadBase64 inválido";
                    return false;
                }

                // Verificar hash sha256 del payload
                var computedSha = Sha256Hex(payloadBytes);
                if (!FixedTimeEqualsHex(computedSha, payloadSha256)) {
                    error = "payloadSha256 no coincide";
                    return false;
                }
            }

            // 4) Verificar firma (siempre)
            var canonical = CanonicalString(
                jobId: jobId,
                clientId: clientId,
                action: action,
                printerType: printerType,
                payloadSha256: payloadSha256,
                iat: ticket.Iat,
                exp: ticket.Exp,
                nonce: nonce
            );

            var expectedSig = HmacSha256Hex(secret, canonical);

            if (!FixedTimeEqualsHex(expectedSig, sig)) {
                error = "firma inválida";
                return false;
            }

            return true;
        }

        // Canonical: jobId|clientId|action|printerType|payloadSha256|iat|exp|nonce
        private static string CanonicalString(
            string jobId,
            string clientId,
            string action,
            string printerType,
            string payloadSha256,
            long iat,
            long exp,
            string nonce)
            => $"{jobId}|{clientId}|{action}|{printerType}|{payloadSha256}|{iat}|{exp}|{nonce}";

        private static string Sha256Hex(byte[] data) {
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string HmacSha256Hex(string secret, string message) {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Comparación en tiempo constante de strings hex (normaliza lower/trim).
        /// </summary>
        private static bool FixedTimeEqualsHex(string aHex, string bHex) {
            if (aHex is null || bHex is null) return false;

            var a = aHex.Trim().ToLowerInvariant();
            var b = bHex.Trim().ToLowerInvariant();

            if (a.Length != b.Length) return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(a),
                Encoding.UTF8.GetBytes(b)
            );
        }

        private void CleanupOldNonces() {
            // Mantén nonces por ~2 ventanas para tolerar skew y retrasos
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_maxSkewSeconds * 2);

            foreach (var kv in _seen) {
                if (kv.Value < cutoff)
                    _seen.TryRemove(kv.Key, out _);
            }
        }
    }
}
