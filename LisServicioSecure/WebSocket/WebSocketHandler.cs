using LisServicioSecure.Models;
using LisServicioSecure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace LisServicioSecure.WebSocket;

public static class WebSocketHandler {
    public static async Task HandleAsync(HttpContext ctx) {
        var ticketB64 = ctx.Request.Query["ticket"].ToString();
        if (string.IsNullOrWhiteSpace(ticketB64)) { ctx.Response.StatusCode = 401; return; }

        SignedTicket ticket;
        try {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(ticketB64));
            ticket = JsonSerializer.Deserialize<SignedTicket>(json)!;
        } catch {
            ctx.Response.StatusCode = 401;
            return;
        }

        var validator = ctx.RequestServices.GetRequiredService<CentralTicketValidator>();
        if (!validator.Validate(ticket, out var payloadBytes, out var err)) {
            ctx.Response.StatusCode = 401;
            return;
        }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    }
}
