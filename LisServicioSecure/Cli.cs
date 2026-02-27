// Cli.cs
// Requisitos:
// - TargetFramework: net8.0-windows
// - PackageReference: System.Management (para printers --set-default)
// - UseWindowsForms=true (para listar impresoras InstalledPrinters)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LisServicioSecure {
    internal static class Cli {
        private static readonly string BasePath = @"C:\ProgramData\Sonda\LisServicioSecure";
        private static readonly string ConfigPath = Path.Combine(BasePath, "appsettings.json");

        // -----------------------------
        // Entry helpers
        // -----------------------------
        public static bool IsCli(string[] args) {
            if (args is null || args.Length == 0) return false;
            var c = (args[0] ?? "").Trim().ToLowerInvariant();
            return c is "printers" or "net" or "agent" or "config" or "help" or "--help" or "-h" or "/?";
        }

        public static int Run(string[] args) {
            Directory.CreateDirectory(BasePath);

            var cmd = (args[0] ?? "").Trim().ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            try {
                return cmd switch {
                    "printers" => Printers(rest),
                    "net" => Net(rest),
                    "agent" => Agent(rest),
                    "config" => Config(rest),
                    "help" or "--help" or "-h" or "/?" => Help(),
                    _ => Fail($"Comando desconocido: {cmd}")
                };
            } catch (Exception ex) {
                // Salida consistente para scripts
                WriteJson(new {
                    ok = false,
                    error = ex.Message,
                    details = ex.ToString()
                });
                return 1;
            }
        }

        private static int Help() {
            Console.WriteLine(@"
LisServicioSecure CLI

CONFIG (C:\ProgramData\Sonda\LisServicioSecure\appsettings.json)
  LisServicioSecure.exe config --get
  LisServicioSecure.exe config --get Printing:NormalPrinter
  LisServicioSecure.exe config --set Printing:NormalPrinter=""Microsoft Print to PDF""
  LisServicioSecure.exe config --set Server:Port=11000 Server:CertPfxPath=""C:\Sonda\certs\lisservicios.pfx""
  LisServicioSecure.exe config --set CentralTicket:Keys:local-2026-02=""SECRET...""
  LisServicioSecure.exe config --ensure
  LisServicioSecure.exe config --backup

PRINTERS
  LisServicioSecure.exe printers --list
  LisServicioSecure.exe printers --set-default ""Nombre Impresora""

NET
  LisServicioSecure.exe net --mac
  LisServicioSecure.exe net --adapters

AGENT
  LisServicioSecure.exe agent --whoami

Notas:
- Para setear valores con secciones usa ':' (ej: Printing:NormalPrinter).
- 'printers --set-default' cambia la impresora predeterminada de Windows (NO tu config).
");
            return 0;
        }

        // -----------------------------
        // printers
        // -----------------------------
        private static int Printers(string[] args) {
            if (args.Length == 0) return Help();

            var op = args[0].Trim().ToLowerInvariant();

            if (op == "--list") {
                var printers = System.Drawing.Printing.PrinterSettings.InstalledPrinters
                    .Cast<string>()
                    .OrderBy(x => x)
                    .ToList();

                WriteJson(new { ok = true, printers });
                return 0;
            }

            if (op == "--set-default") {
                if (args.Length < 2) return Fail("Falta nombre de impresora. Ej: printers --set-default \"HP Laser\"");

                var name = args[1].Trim();
                SetDefaultPrinterWmi(name);

                WriteJson(new { ok = true, message = "Default printer seteada (Windows)", printer = name });
                return 0;
            }

            return Fail("Opción printers inválida.");
        }

        private static void SetDefaultPrinterWmi(string printerName) {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
            foreach (ManagementObject printer in searcher.Get()) {
                var name = (printer["Name"]?.ToString() ?? "");
                if (string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase)) {
                    printer.InvokeMethod("SetDefaultPrinter", null);
                    return;
                }
            }
            throw new InvalidOperationException($"No se encontró la impresora: {printerName}");
        }

        // -----------------------------
        // net
        // -----------------------------
        private static int Net(string[] args) {
            if (args.Length == 0) return Help();

            var op = args[0].Trim().ToLowerInvariant();

            if (op == "--mac") {
                var mac = GetPrimaryMac();
                WriteJson(new { ok = true, mac });
                return 0;
            }

            if (op == "--adapters") {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => new {
                        name = n.Name,
                        desc = n.Description,
                        status = n.OperationalStatus.ToString(),
                        mac = FormatMac(n.GetPhysicalAddress()),
                        type = n.NetworkInterfaceType.ToString()
                    })
                    .ToList();

                WriteJson(new { ok = true, adapters });
                return 0;
            }

            return Fail("Opción net inválida.");
        }

        private static string GetPrimaryMac() {
            var pa = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetPhysicalAddress())
                .FirstOrDefault(x => x != null && x.GetAddressBytes().Length > 0);

            return pa == null ? "" : FormatMac(pa);
        }

        private static string FormatMac(PhysicalAddress pa)
            => string.Join("-", pa.GetAddressBytes().Select(b => b.ToString("X2")));

        // -----------------------------
        // agent
        // -----------------------------
        private static int Agent(string[] args) {
            if (args.Length == 0) return Help();

            var op = args[0].Trim().ToLowerInvariant();

            if (op == "--whoami") {
                var user = WindowsIdentity.GetCurrent()?.Name ?? "unknown";
                WriteJson(new { ok = true, user });
                return 0;
            }

            return Fail("Opción agent inválida.");
        }

        // -----------------------------
        // config
        // -----------------------------
        private static int Config(string[] args) {
            if (args.Length == 0) return Help();

            var op = args[0].Trim().ToLowerInvariant();

            if (op == "--ensure") {
                var root = LoadConfigOrEmpty();
                EnsureDefaults(root);
                SaveConfig(root, makeBackup: true);
                WriteJson(new { ok = true, message = "Config ensured", path = ConfigPath });
                return 0;
            }

            if (op == "--backup") {
                MakeBackupIfExists();
                WriteJson(new { ok = true, message = "Backup creado", backup = ConfigPath + ".bak" });
                return 0;
            }

            if (op == "--get") {
                var key = args.Length >= 2 ? args[1].Trim() : null;
                var root = LoadConfigOrEmpty();

                if (string.IsNullOrWhiteSpace(key)) {
                    WriteJson(new { ok = true, config = root });
                    return 0;
                }

                var node = GetByPath(root, key!);
                WriteJson(new { ok = true, key, value = node });
                return 0;
            }

            if (op == "--set") {
                if (args.Length < 2)
                    return Fail("Falta asignación. Ej: config --set Printing:NormalPrinter=\"Microsoft Print to PDF\"");

                var root = LoadConfigOrEmpty();

                // Permite múltiples asignaciones: config --set A=1 B=2 C:D=3
                var assigns = args.Skip(1).ToArray();

                var applied = new List<string>();
                foreach (var a in assigns) {
                    var (k, raw) = ParseAssign(a);
                    var node = ParseValueNode(raw);
                    SetByPath(root, k, node);
                    applied.Add(k);
                }

                SaveConfig(root, makeBackup: true);
                WriteJson(new { ok = true, message = "Config guardada", updated = applied });
                return 0;
            }

            return Fail("Opción config inválida.");
        }

        private static JsonObject LoadConfigOrEmpty() {
            Directory.CreateDirectory(BasePath);

            if (!File.Exists(ConfigPath))
                return new JsonObject();

            var json = File.ReadAllText(ConfigPath);
            if (string.IsNullOrWhiteSpace(json))
                return new JsonObject();

            var node = JsonNode.Parse(json);
            return node as JsonObject ?? new JsonObject();
        }

        private static void SaveConfig(JsonObject root, bool makeBackup) {
            Directory.CreateDirectory(BasePath);

            if (makeBackup) MakeBackupIfExists();

            // Mantén el JSON legible
            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        private static void MakeBackupIfExists() {
            if (File.Exists(ConfigPath)) {
                var bak = ConfigPath + ".bak";
                File.Copy(ConfigPath, bak, overwrite: true);
            }
        }

        // Crea estructura mínima de tu appsettings, sin pisar lo existente
        private static void EnsureDefaults(JsonObject root) {
            // Server
            var server = EnsureObject(root, "Server");
            EnsureValue(server, "Port", JsonValue.Create(11000));
            EnsureValue(server, "CertPfxPath", JsonValue.Create(@"C:\Sonda\certs\lisservicios.pfx"));
            EnsureValue(server, "CertPassword", JsonValue.Create("Sonda"));

            // Printing
            var printing = EnsureObject(root, "Printing");
            EnsureValue(printing, "NormalPrinter", JsonValue.Create("Microsoft Print to PDF"));
            EnsureValue(printing, "ZebraPrinter", JsonValue.Create("ZEBRA_D220"));
            EnsureValue(printing, "MaxPayloadBytes", JsonValue.Create(5242880));

            // CentralTicket
            var ct = EnsureObject(root, "CentralTicket");
            EnsureValue(ct, "MaxSkewSeconds", JsonValue.Create(120));
            var keys = EnsureObject(ct, "Keys");
            // no insertamos una key por defecto si no existe, porque es sensible
        }

        private static JsonObject EnsureObject(JsonObject parent, string key) {
            if (parent[key] is JsonObject obj) return obj;
            obj = new JsonObject();
            parent[key] = obj;
            return obj;
        }

        private static void EnsureValue(JsonObject parent, string key, JsonNode? value) {
            if (parent[key] is null)
                parent[key] = value;
        }

        private static (string key, string rawValue) ParseAssign(string assign) {
            var eq = assign.IndexOf('=');
            if (eq <= 0)
                throw new ArgumentException($"Asignación inválida: {assign}. Usa Key=Value");

            var key = assign[..eq].Trim();
            var val = assign[(eq + 1)..].Trim();

            // quita comillas externas si vienen
            if (val.Length >= 2 &&
                ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                 (val.StartsWith("'") && val.EndsWith("'")))) {
                val = val[1..^1];
            }

            return (key, val);
        }

        private static JsonNode? ParseValueNode(string raw) {
            if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
                return null;

            if (bool.TryParse(raw, out var b))
                return JsonValue.Create(b);

            // int / long
            if (int.TryParse(raw, out var i))
                return JsonValue.Create(i);

            if (long.TryParse(raw, out var l))
                return JsonValue.Create(l);

            // double con punto
            if (double.TryParse(raw,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d)) {
                return JsonValue.Create(d);
            }

            // default string
            return JsonValue.Create(raw);
        }

        private static JsonNode? GetByPath(JsonObject root, string path) {
            var parts = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
            JsonNode? cur = root;

            foreach (var p in parts) {
                if (cur is JsonObject obj) {
                    obj.TryGetPropertyValue(p, out cur);
                } else {
                    return null;
                }
            }
            return cur;
        }

        private static void SetByPath(JsonObject root, string path, JsonNode? value) {
            var parts = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) throw new ArgumentException("Path vacío");

            JsonObject cur = root;

            for (int i = 0; i < parts.Length - 1; i++) {
                var p = parts[i];
                if (cur[p] is not JsonObject child) {
                    child = new JsonObject();
                    cur[p] = child;
                }
                cur = child;
            }

            cur[parts[^1]] = value;
        }

        // -----------------------------
        // output helpers
        // -----------------------------
        private static void WriteJson(object o) {
            var json = JsonSerializer.Serialize(o, JsonOpts());
            Console.WriteLine(json);
        }

        private static JsonSerializerOptions JsonOpts() => new() {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static int Fail(string msg) {
            WriteJson(new { ok = false, error = msg });
            return 2;
        }
    }
}
