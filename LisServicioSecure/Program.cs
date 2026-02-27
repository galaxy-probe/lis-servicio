using LisServicioSecure;
using LisServicioSecure.Models;
using LisServicioSecure.Printing;
using LisServicioSecure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;


// =======================
// MODO CLI (ANTES DE TODO)
// =======================
//Console.WriteLine("ARGS=" + string.Join(" | ", args));
if (args.Length > 0 && Cli.IsCli(args)) {
    var code = Cli.Run(args);
    Environment.Exit(code);
}

var basePath = @"C:\ProgramData\Sonda\LisServicioSecure";
Directory.CreateDirectory(basePath);
Directory.CreateDirectory(Path.Combine(basePath, "logs"));

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(basePath, "logs", "lissecure-.log"),
        rollingInterval: RollingInterval.Day,
        shared: true)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// ✅ Servicio Windows + Serilog
builder.Host.UseWindowsService(o => o.ServiceName = "LisServicioSecure");
builder.Host.UseSerilog();

// ✅ Config desde ProgramData
builder.Configuration
    .SetBasePath(basePath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// ✅ Kestrel HTTPS (WSS)
builder.WebHost.ConfigureKestrel((ctx, kestrel) => {
    var port = ctx.Configuration.GetValue<int>("Server:Port");
    var pfx = ctx.Configuration["Server:CertPfxPath"];
    var pwd = ctx.Configuration["Server:CertPassword"];

    if (port <= 0) throw new InvalidOperationException("Server:Port mal configurado");
    if (string.IsNullOrWhiteSpace(pfx)) throw new InvalidOperationException("Server:CertPfxPath requerido");
    if (!File.Exists(pfx)) throw new FileNotFoundException("No existe el PFX", pfx);

    kestrel.ListenAnyIP(port, listen => listen.UseHttps(pfx, pwd));
});

// ✅ Options + servicios
builder.Services.Configure<PrintingOptions>(builder.Configuration.GetSection("Printing"));

// ✅ Central ticket (keys + validator)
// Asegúrate de que appsettings.json tenga sección CentralTicket (MaxSkewSeconds) y la fuente de keys que use CentralTicketKeys.
builder.Services.AddSingleton<CentralTicketKeys>();
builder.Services.AddSingleton<CentralTicketValidator>();

builder.Services.AddSingleton<ZplPrinter>();
builder.Services.AddSingleton<PdfPrinter>();

var app = builder.Build();

// ✅ Ruta raíz para probar HTTPS en Chrome
app.MapGet("/", () => Results.Ok("LisServicioSecure OK"));

// ✅ WebSockets
app.UseWebSockets();

app.Map("/ws", async (HttpContext ctx) => {
    if (!ctx.WebSockets.IsWebSocketRequest) {
        ctx.Response.StatusCode = 400;
        return;
    }

    // 🔐 Ticket via querystring (JSON URL-encoded)
    var ticketParam = ctx.Request.Query["ticket"].ToString();
    if (string.IsNullOrWhiteSpace(ticketParam)) {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("ticket requerido");
        return;
    }

    SignedTicket ticket;
    try {
        var ticketJson = Uri.UnescapeDataString(ticketParam);

        ticket = JsonSerializer.Deserialize<SignedTicket>(ticketJson, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        })!;
        if (ticket is null) throw new Exception("ticket null");
    } catch (Exception ex) {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("ticket inválido: " + ex.Message);
        return;
    }

    // ✅ Validar ticket (firma/exp/replay/payload)
    var validator = ctx.RequestServices.GetRequiredService<CentralTicketValidator>();
    if (!validator.Validate(ticket, out var payloadBytes, out var err)) {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync(err);
        return;
    }

    // ✅ Resolver printers desde DI
    var zplPrinter = ctx.RequestServices.GetRequiredService<ZplPrinter>();
    var pdfPrinter = ctx.RequestServices.GetRequiredService<PdfPrinter>();

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var buffer = new byte[64 * 1024];

    while (!ctx.RequestAborted.IsCancellationRequested && ws.State == WebSocketState.Open) {
        var msg = await ReceiveTextAsync(ws, buffer, ctx.RequestAborted);
        if (msg is null) break;

        WsResponse resp;

        try {
            var req = JsonSerializer.Deserialize<WsRequest>(msg, new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            }) ?? new WsRequest();

            var accion = (req.Accion ?? "").Trim().ToLowerInvariant();

            // ✅ Enforzar que la acción del mensaje coincida con el ticket
            var ticketAction = (ticket.Action ?? "").Trim().ToLowerInvariant();
            if (!string.Equals(ticketAction, accion, StringComparison.OrdinalIgnoreCase)) {
                resp = new WsResponse { Ok = false, Mensaje = "Acción no coincide con ticket" };
                await SendJsonAsync(ws, resp, ctx.RequestAborted);
                continue;
            }

            switch (accion) {
                case "getmac":
                    resp = new WsResponse {
                        Ok = true,
                        Mensaje = "OK",
                        Data = NetworkInfo.GetActiveMacAddress()
                    };
                    break;

                case "print":
                    if (req.Tickets == null || req.Tickets.Count == 0) {
                        resp = new WsResponse { Ok = false, Mensaje = "Tickets requeridos" };
                        break;
                    }

                    foreach (var t in req.Tickets) {
                        if (!validator.Validate(t, out var payloadBytes2, out var err2)) {
                            resp = new WsResponse { Ok = false, Mensaje = $"Ticket inválido: {err2}" };
                            goto default;
                        }

                        var act = (t.Action ?? "").Trim().ToLowerInvariant();

                        if (act == "print_zpl") {
                            // si tu ZplPrinter espera base64 string, conviertes:
                            var b64 = Convert.ToBase64String(payloadBytes2);
                            var r = zplPrinter.PrintBase64Zpl(b64);
                            if (!r.ok) { resp = new WsResponse { Ok = false, Mensaje = r.msg }; goto errorImpresion; }
                        } else if (act == "print_pdf") {
                            var b64 = Convert.ToBase64String(payloadBytes2);
                            var tipo = string.IsNullOrWhiteSpace(t.PrinterType) ? "normal" : t.PrinterType; // o usa otro campo
                            var r = pdfPrinter.PrintBase64Pdf(b64, tipo);
                            if (!r.ok) { resp = new WsResponse { Ok = false, Mensaje = r.msg }; goto errorImpresion; }
                        } else {
                            //resp = new WsResponse { Ok = false, Mensaje = $"Acción ticket no soportada: {act}" };
                            goto default;
                        }
                    }

                    resp = new WsResponse { Ok = true, Mensaje = "OK" };
                    break;
                errorImpresion:
                    break;
                default:
                    resp = new WsResponse {
                        Ok = false,
                        Mensaje = $"Acción no soportada: {accion}"
                    };
                    break;
            }
        } catch (Exception ex) {
            resp = new WsResponse {
                Ok = false,
                Mensaje = "Error procesando mensaje: " + ex.Message
            };
        }

        await SendJsonAsync(ws, resp, ctx.RequestAborted);
    }

    if (ws.State == WebSocketState.Open)
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", ctx.RequestAborted);
});

app.Run();

static async Task<string?> ReceiveTextAsync(WebSocket ws, byte[] buffer, CancellationToken ct) {
    var sb = new StringBuilder();
    WebSocketReceiveResult result;

    do {
        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

        if (result.MessageType == WebSocketMessageType.Close)
            return null;

        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }
    while (!result.EndOfMessage);

    return sb.ToString();
}

static Task SendJsonAsync(WebSocket ws, object payload, CancellationToken ct) {
    var json = JsonSerializer.Serialize(payload);
    var bytes = Encoding.UTF8.GetBytes(json);
    return ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
}
