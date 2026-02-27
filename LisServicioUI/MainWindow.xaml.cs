using System.Drawing.Printing;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace LisServicioUI {
    public partial class MainWindow : Window {
        private const string ServiceName = "Sonda.WsPrint";

        private static readonly string BasePath = @"C:\Sonda\WsPrint";
        private static readonly string ConfigPath = Path.Combine(BasePath, "appsettings.json");
        private static readonly string LogDir = Path.Combine(BasePath, "logs");

        private readonly DispatcherTimer _uiTimer = new();
        private long _lastLogPos = 0;
        private string _currentLogFile = "";

        public MainWindow() {
            InitializeComponent();

            Directory.CreateDirectory(BasePath);
            Directory.CreateDirectory(LogDir);

            LoadPrinters();
            LoadConfigToUi();

            _uiTimer.Interval = TimeSpan.FromMilliseconds(800);
            _uiTimer.Tick += (_, __) => {
                RefreshServiceStatus();
                TailLog();
            };
            _uiTimer.Start();

            RefreshServiceStatus();
        }

        private void LoadPrinters() {
            CmbNormal.Items.Clear();
            CmbZebra.Items.Clear();

            foreach (string p in PrinterSettings.InstalledPrinters) {
                CmbNormal.Items.Add(p);
                CmbZebra.Items.Add(p);
            }
        }

        private void RefreshServiceStatus() {
            try {
                using var sc = new ServiceController(ServiceName);
                TxtStatus.Text = sc.Status.ToString();
            } catch {
                TxtStatus.Text = "No instalado / sin permisos";
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e) {
            TryServiceAction(sc => {
                if (sc.Status == ServiceControllerStatus.Running) return;
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                AppendConsoleLine("UI: Servicio iniciado");
            });
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e) {
            TryServiceAction(sc => {
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                AppendConsoleLine("UI: Servicio detenido");
            });
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e) {
            TryServiceAction(sc => {
                if (sc.Status != ServiceControllerStatus.Stopped) {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                AppendConsoleLine("UI: Servicio reiniciado");
            });
        }

        private void TryServiceAction(Action<ServiceController> action) {
            try {
                using var sc = new ServiceController(ServiceName);
                action(sc);
                RefreshServiceStatus();
            } catch (Exception ex) {
                AppendConsoleLine("UI ERROR: " + ex.Message);
                MessageBox.Show(ex.Message, "Error controlando servicio (¿Ejecutaste como Admin?)",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) {
            try {
                var normal = CmbNormal.SelectedItem?.ToString() ?? "";
                var zebra = CmbZebra.SelectedItem?.ToString() ?? "";

                var cfg = ReadConfig();
                cfg.Printing.NormalPrinter = normal;
                cfg.Printing.ZebraPrinter = zebra;

                WriteConfigAtomic(cfg);

                AppendConsoleLine($"UI: Config guardada. Normal='{normal}' Zebra='{zebra}'");
            } catch (Exception ex) {
                AppendConsoleLine("UI ERROR guardando config: " + ex.Message);
                MessageBox.Show(ex.Message, "Error guardando configuración",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadConfigToUi() {
            try {
                if (!File.Exists(ConfigPath)) {
                    var fresh = new AppConfig();
                    WriteConfigAtomic(fresh);
                }

                var cfg = ReadConfig();
                SelectIfExists(CmbNormal, cfg.Printing.NormalPrinter);
                SelectIfExists(CmbZebra, cfg.Printing.ZebraPrinter);
            } catch (Exception ex) {
                AppendConsoleLine("UI ERROR cargando config: " + ex.Message);
            }
        }

        private static void SelectIfExists(System.Windows.Controls.ComboBox cmb, string value) {
            if (string.IsNullOrWhiteSpace(value)) return;
            for (int i = 0; i < cmb.Items.Count; i++) {
                if (string.Equals(cmb.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase)) {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
        }

        // ========= Log console (tail) =========

        private void TailLog() {
            try {
                var newest = GetNewestLogFile();
                if (string.IsNullOrEmpty(newest)) return;

                if (!string.Equals(newest, _currentLogFile, StringComparison.OrdinalIgnoreCase)) {
                    _currentLogFile = newest;
                    _lastLogPos = 0;
                    AppendConsoleLine($"UI: leyendo log -> {Path.GetFileName(_currentLogFile)}");
                }

                using var fs = new FileStream(_currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (_lastLogPos > fs.Length) _lastLogPos = 0;

                fs.Seek(_lastLogPos, SeekOrigin.Begin);

                using var sr = new StreamReader(fs, Encoding.UTF8);
                var newText = sr.ReadToEnd();
                _lastLogPos = fs.Position;

                if (!string.IsNullOrEmpty(newText)) {
                    TxtConsole.AppendText(newText);
                    TxtConsole.ScrollToEnd();
                }
            } catch {
                // silencioso (log puede rotar / bloquearse momentáneamente)
            }
        }

        private static string GetNewestLogFile() {
            if (!Directory.Exists(LogDir)) return "";
            var files = Directory.GetFiles(LogDir, "wsprint-*.log");
            if (files.Length == 0) return "";
            Array.Sort(files);
            return files[^1];
        }

        private void AppendConsoleLine(string line) {
            TxtConsole.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            TxtConsole.ScrollToEnd();
        }

        // ========= Config (JSON) =========

        private static AppConfig ReadConfig() {
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            }) ?? new AppConfig();
        }

        private static void WriteConfigAtomic(AppConfig cfg) {
            var tmp = ConfigPath + ".tmp";
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json, Encoding.UTF8);

            // Reemplazo atómico
            File.Copy(tmp, ConfigPath, overwrite: true);
            File.Delete(tmp);
        }
    }

    public sealed class AppConfig {
        public PrintingSection Printing { get; set; } = new();
        public LoggingSection Logging { get; set; } = new();
    }

    public sealed class PrintingSection {
        public string NormalPrinter { get; set; } = "";
        public string ZebraPrinter { get; set; } = "";
    }

    public sealed class LoggingSection {
        public string LogPath { get; set; } = @"C:\ProgramData\Sonda\WsPrint\logs\wsprint-.log";
    }
}
