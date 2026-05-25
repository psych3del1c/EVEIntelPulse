// EVE IntelPulse
// Copyright (c) 2026 Arnold Olympus
// Licensed under GPL v3. See LICENSE file in the repository.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EVEIntelPulse
{
    public partial class MainForm : Form
    {
        // ------------------------------------------------------------------
        // Application constants
        // ------------------------------------------------------------------
        private const string CurrentVersion = "1.0.0";               // Public release version
        private const string GitHubRepoUrl = "https://github.com/psych3del1c/EVE-IntelPulse";  // TODO: replace before release
        private const string UpdateCheckUrl = "https://api.github.com/repos/psych3del1c/EVEIntelPulse/releases/latest";
        private const string CharacterZkillUrl = "https://zkillboard.com/character/92604044/";

        // Default hotkey
        private const string DefaultHotkey = "alt+q";

        // Timing values (milliseconds)
        private const int ClipboardClearDelayMs = 100;
        private const int SimulateCtrlCDelayMs = 600;
        private const int ManualModeDelayMs = 200;
        private const int MaxRetriesClipboard = 3;
        private const int RetryDelayMs = 150;
        private const int UpdateCheckIntervalHours = 6;
        private const int EveWindowRestoreDelayMs = 200;
        private const int FooterMarginBottom = 10;                    // Pixels from the window bottom

        // ------------------------------------------------------------------
        // Private fields
        // ------------------------------------------------------------------
        private readonly string configDir;
        private readonly string configFile;
        private readonly string dbFile;
        private string characterHotkey = "";
        private bool manualCopyMode;
        private bool startMinimized;
        private Dictionary<string, Dictionary<string, string>> eveDb = new();

        private NotifyIcon? trayIcon;
        private Bitmap? trayBitmap;    // Fallback bitmap for tray icon

        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------
        public MainForm()
        {
            configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EVE_IntelPulse");
            Directory.CreateDirectory(configDir);
            configFile = Path.Combine(configDir, "config.json");
            dbFile = Path.Combine(configDir, "eve_universe.json");

            ExtractEmbeddedResource("EVEIntelPulse.eve_universe.json", dbFile);

            Logger.Initialize(Path.Combine(configDir, "app.log"));
            Logger.Write("[INFO] Application started");

            LoadConfig();
            InitializeComponent();
            SetupWindowIcon();
            LoadEveDatabase();
            SetupTrayIcon();
            SetupHotkeys();
            UpdateHotkeyLabel();

            manualCopyCheckBox.Checked = manualCopyMode;
            startMinimizedCheckBox.Checked = startMinimized;
            if (startMinimized)
                this.Shown += (_, _) => MinimizeToTray();

            // Footer positioning after the window is fully loaded
            this.Load += (_, _) =>
            {
                footerLayout.PerformLayout();
                authorPrefixLabel.Left = topLine.Left;
                int footerX = (ClientSize.Width - footerLayout.Width) / 2;
                int footerY = ClientSize.Height - footerLayout.Height - FooterMarginBottom;
                footerLayout.Location = new Point(footerX, footerY);
            };

            // Start asynchronous update check
            Task.Run(CheckForUpdatesAsync);

            Logger.Write("[INFO] Initialization complete");
        }

        // ------------------------------------------------------------------
        // Resource helpers
        // ------------------------------------------------------------------
        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            if (File.Exists(outputPath))
                return;

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.Write($"[WARN] Embedded resource '{resourceName}' not found");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var fileStream = File.Create(outputPath);
            stream.CopyTo(fileStream);
            Logger.Write($"[OK] Extracted '{resourceName}' to '{outputPath}'");
        }

        private void SetupWindowIcon()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("EVEIntelPulse.eye_icon.ico");
                if (stream != null)
                    Icon = new Icon(stream);
            }
            catch (Exception ex)
            {
                Logger.Write($"[WARN] Failed to load window icon: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // UI update helpers
        // ------------------------------------------------------------------
        private void UpdateHotkeyLabel()
        {
            if (hotkeyLabel != null)
                hotkeyLabel.Text = $"Current hotkey: {characterHotkey.ToUpper()}";
        }

        // ------------------------------------------------------------------
        // Event handlers
        // ------------------------------------------------------------------
        private void HotkeyEntry_Leave(object? sender, EventArgs e) =>
            characterHotkey = hotkeyEntry!.Text.Trim().ToLower();

        private void ApplyHotkey_Click(object? sender, EventArgs e) => ApplyCharacterHotkey();

        private void ManualCopyCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            manualCopyMode = manualCopyCheckBox.Checked;
            SaveConfig();
            Logger.Write($"[INFO] Manual copy mode toggled: {manualCopyMode}");
        }

        private void StartMinimizedCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            startMinimized = startMinimizedCheckBox.Checked;
            SaveConfig();
            Logger.Write($"[INFO] Start minimized toggled: {startMinimized}");
        }

        private void TrayBtn_Click(object? sender, EventArgs e) => MinimizeToTray();
        private void ExitBtn_Click(object? sender, EventArgs e) => Application.Exit();

        private void ApplyCharacterHotkey()
        {
            if (hotkeyEntry == null) return;
            string newHotkey = hotkeyEntry.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(newHotkey)) return;

            try
            {
                characterHotkey = newHotkey;
                UpdateHotkeyLabel();
                SetupHotkeys();
                SaveConfig();
                Logger.Write($"[INFO] Hotkey changed to '{characterHotkey}'");
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] ApplyHotkey failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Configuration (JSON)
        // ------------------------------------------------------------------
        private void LoadConfig()
        {
            Logger.Write($"[INFO] Loading config: {configFile}");
            try
            {
                if (!File.Exists(configFile))
                {
                    characterHotkey = DefaultHotkey;
                    manualCopyMode = false;
                    startMinimized = false;
                    Logger.Write("[INFO] Config file not found, using defaults");
                    UpdateHotkeyLabel();
                    return;
                }

                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(configFile));
                if (config == null)
                {
                    characterHotkey = DefaultHotkey;
                    return;
                }

                characterHotkey = config.TryGetValue("character_hotkey", out var hk) && hk.ValueKind == JsonValueKind.String
                    ? hk.GetString()!
                    : DefaultHotkey;

                manualCopyMode = config.TryGetValue("manual_copy", out var mc) && mc.ValueKind == JsonValueKind.True;
                startMinimized = config.TryGetValue("start_minimized", out var sm) && sm.ValueKind == JsonValueKind.True;

                Logger.Write($"[INFO] Config loaded: Hotkey='{characterHotkey}', ManualCopy={manualCopyMode}, StartMinimized={startMinimized}");
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] LoadConfig failed: {ex.Message}");
                characterHotkey = DefaultHotkey;
                manualCopyMode = false;
                startMinimized = false;
            }
            UpdateHotkeyLabel();
        }

        private void SaveConfig()
        {
            try
            {
                var config = new Dictionary<string, object>
                {
                    ["character_hotkey"] = characterHotkey,
                    ["manual_copy"] = manualCopyMode,
                    ["start_minimized"] = startMinimized
                };

                // Preserve last_update_check if it exists
                if (File.Exists(configFile))
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(configFile));
                    if (existing != null && existing.TryGetValue("last_update_check", out var lastCheck))
                        config["last_update_check"] = lastCheck.GetString()!;
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configFile, JsonSerializer.Serialize(config, options));
                Logger.Write("[INFO] Config saved successfully");
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] SaveConfig failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // EVE Universe Database
        // ------------------------------------------------------------------
        private void LoadEveDatabase()
        {
            string dbPath = dbFile;
            if (!File.Exists(dbPath))
            {
                dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eve_universe.json");
                if (!File.Exists(dbPath))
                {
                    Logger.Write($"[WARN] eve_universe.json not found at '{dbFile}' nor beside exe");
                    return;
                }
            }

            Logger.Write($"[INFO] Loading database: {dbPath}");
            try
            {
                using var jsonDoc = JsonDocument.Parse(File.ReadAllText(dbPath));
                var root = jsonDoc.RootElement;

                eveDb.Clear();
                foreach (var category in root.EnumerateObject())
                {
                    if (category.Name.Equals("metadata", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string cleanKey = category.Name.Trim().TrimEnd('s');
                    var cleanDict = new Dictionary<string, string>();

                    foreach (var entry in category.Value.EnumerateObject())
                    {
                        string key = entry.Name.Trim().ToLowerInvariant();
                        string value = entry.Value.ValueKind switch
                        {
                            JsonValueKind.String => entry.Value.GetString()!,
                            JsonValueKind.Number => entry.Value.GetRawText(),
                            _ => entry.Value.ToString()
                        };
                        cleanDict[key] = value;
                    }
                    eveDb[cleanKey] = cleanDict;
                }
                Logger.Write($"[OK] Database loaded: {eveDb.GetValueOrDefault("system")?.Count ?? 0} systems");
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] Database load failed: {ex.Message}");
            }
        }

        private (string? type, string? id) FindEveEntry(string name)
        {
            if (string.IsNullOrEmpty(name) || eveDb == null)
                return (null, null);

            string searchName = name.Trim().ToLowerInvariant();
            foreach (string category in new[] { "system", "constellation", "region", "character", "corporation", "alliance" })
            {
                if (eveDb.TryGetValue(category, out var dict) && dict.ContainsKey(searchName))
                    return (category, dict[searchName]);
            }
            return (null, null);
        }

        // ------------------------------------------------------------------
        // Global hotkey
        // ------------------------------------------------------------------
        private void SetupHotkeys()
        {
            Logger.Write($"[INFO] Registering hotkey '{characterHotkey}'");
            try
            {
                KeyboardHook.UnhookAll();
                KeyboardHook.SetHotkeyAndCallback(characterHotkey, OnHotkeyTriggered);
                KeyboardHook.InstallHook();
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] SetupHotkeys failed: {ex.Message}");
            }
        }

        private void OnHotkeyTriggered()
        {
            Logger.Write("[INFO] Hotkey triggered");
            BeginInvoke(OpenCharacterLogic);
        }

        // ------------------------------------------------------------------
        // Core logic (clipboard + zkillboard)
        // ------------------------------------------------------------------
        private string TryGetClipboardText()
        {
            for (int i = 0; i < MaxRetriesClipboard; i++)
            {
                try
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
                catch (Exception ex)
                {
                    Logger.Write($"[ERROR] Clipboard read attempt {i + 1} failed: {ex.Message}");
                }

                if (i < MaxRetriesClipboard - 1)
                    Thread.Sleep(RetryDelayMs);
            }
            return "";
        }

        private void OpenCharacterLogic()
        {
            try
            {
                Logger.Write($"[INFO] OpenCharacterLogic start (manual mode = {manualCopyMode})");

                if (!manualCopyMode)
                {
                    IntPtr hwnd = GetActiveEveWindowOrActivate();
                    if (hwnd == IntPtr.Zero)
                    {
                        Logger.Write("[WARN] EVE window not found");
                        return;
                    }

                    Logger.Write($"[INFO] EVE window found: {hwnd}");

                    try
                    {
                        Clipboard.Clear();
                        Logger.Write("[INFO] Clipboard cleared");
                    }
                    catch (Exception ex)
                    {
                        Logger.Write($"[ERROR] Clipboard.Clear failed: {ex.Message}");
                        try { Clipboard.SetDataObject(""); } catch { }
                    }

                    Thread.Sleep(ClipboardClearDelayMs);
                    SimulateCtrlC();
                    Thread.Sleep(SimulateCtrlCDelayMs);
                }
                else
                {
                    Logger.Write("[INFO] Manual mode: waiting for pre-copied text");
                    Thread.Sleep(ManualModeDelayMs);
                }

                string rawText = TryGetClipboardText();
                Logger.Write($"[INFO] Clipboard returned: '{rawText}'");

                string name = string.IsNullOrEmpty(rawText) ? "" : rawText.Trim().Replace("*", "").Trim();
                if (name.Length > 90)
                    name = name[..90];

                Logger.Write($"[INFO] Processed name: '{name}'");

                if (name.Length < 2)
                {
                    Logger.Write("[WARN] Clipboard empty or too short");
                    return;
                }

                var (entryType, eveId) = FindEveEntry(name);
                string url;
                if (eveId != null)
                {
                    url = $"https://zkillboard.com/{entryType}/{eveId}/";
                    Logger.Write($"[INFO] Found in DB: {entryType}/{eveId}");
                }
                else
                {
                    string encoded = name.Replace("  ", "%20");
                    url = $"https://zkillboard.com/search/{encoded}/";
                    Logger.Write($"[INFO] Not found, search URL: {url}");
                }

                Logger.Write($"[INFO] Opening browser: {url}");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] OpenCharacterLogic failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // EVE window helpers
        // ------------------------------------------------------------------
        private static IntPtr GetActiveEveWindowOrActivate()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && IsWindowVisible(fg) && IsEveWindow(fg))
                return fg;

            IntPtr found = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd) && IsEveWindow(hwnd))
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found == IntPtr.Zero)
                return IntPtr.Zero;

            if (IsIconic(found))
                ShowWindow(found, SW_RESTORE);
            SetForegroundWindow(found);
            Thread.Sleep(EveWindowRestoreDelayMs);
            return found;
        }

        private static bool IsEveWindow(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString().ToLowerInvariant();
            return title.Contains("eve -") ||
                   title.Contains("eve online") ||
                   title.Contains("eve client") ||
                   title.Contains("- eve") ||
                   title.Contains("eve -[") ||
                   title.Contains("eve");
        }

        private static void SimulateCtrlC()
        {
            const int keyDownFlag = 0x0000;
            const int keyUpFlag = 0x0002;
            const int ctrl = 0x11;
            const int c = 0x43;
            const int stepMs = 50;
            const int finalStepMs = 100;

            keybd_event(ctrl, 0, keyDownFlag, UIntPtr.Zero); Thread.Sleep(stepMs);
            keybd_event(c, 0, keyDownFlag, UIntPtr.Zero); Thread.Sleep(stepMs);
            keybd_event(c, 0, keyUpFlag, UIntPtr.Zero); Thread.Sleep(stepMs);
            keybd_event(ctrl, 0, keyUpFlag, UIntPtr.Zero); Thread.Sleep(finalStepMs);
        }

        // ------------------------------------------------------------------
        // System tray
        // ------------------------------------------------------------------
        private void SetupTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (_, _) => ShowFromTray());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Application.Exit());

            trayIcon = new NotifyIcon
            {
                Icon = LoadTrayIconFromResource(),
                ContextMenuStrip = menu,
                Text = "EVE IntelPulse",
                Visible = false
            };
            trayIcon.DoubleClick += (_, _) => ShowFromTray();
            Logger.Write("[INFO] Tray icon initialized");
        }

        private Icon LoadTrayIconFromResource()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EVEIntelPulse.eye_icon.ico");
                if (stream != null)
                    return new Icon(stream);
            }
            catch (Exception ex)
            {
                Logger.Write($"[WARN] Failed to load tray icon: {ex.Message}");
            }

            // Fallback: a simple red circle with white dot
            trayBitmap = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(trayBitmap))
            {
                g.FillEllipse(Brushes.DarkRed, 8, 20, 48, 24);
                g.FillEllipse(Brushes.White, 26, 26, 12, 12);
            }
            return Icon.FromHandle(trayBitmap.GetHicon());
        }

        private void MinimizeToTray()
        {
            WindowState = FormWindowState.Minimized;
            Hide();
            if (trayIcon != null) trayIcon.Visible = true;
            Logger.Write("[INFO] Minimized to tray");
        }

        private void ShowFromTray()
        {
            if (trayIcon != null) trayIcon.Visible = false;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            Logger.Write("[INFO] Restored from tray");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Logger.Write("[INFO] Application closing");
            trayIcon?.Dispose();
            trayBitmap?.Dispose();
            KeyboardHook.UnhookAll();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------
        // Update checker
        // ------------------------------------------------------------------
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                // Respect check interval
                if (File.Exists(configFile))
                {
                    var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(configFile));
                    if (config != null && config.TryGetValue("last_update_check", out var lastCheckElement))
                    {
                        string? lastCheckStr = lastCheckElement.GetString();
                        if (DateTime.TryParse(lastCheckStr, out DateTime lastCheck) &&
                            (DateTime.UtcNow - lastCheck).TotalHours < UpdateCheckIntervalHours)
                            return;
                    }
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "EVEIntelPulse");
                string response = await client.GetStringAsync(UpdateCheckUrl);
                using var doc = JsonDocument.Parse(response);
                string? latestTag = doc.RootElement.GetProperty("tag_name").GetString();
                string? htmlUrl = doc.RootElement.GetProperty("html_url").GetString();

                SaveLastUpdateCheck();

                if (latestTag != null && latestTag != $"v{CurrentVersion}")
                {
                    BeginInvoke(new Action(() =>
                    {
                        versionLabel.Text = $"Update available: {latestTag}";
                        versionLabel.LinkColor = Color.FromArgb(100, 149, 237);
                        versionLabel.ActiveLinkColor = Color.FromArgb(100, 149, 237);
                        versionLabel.VisitedLinkColor = Color.FromArgb(100, 149, 237);
                        versionLabel.LinkBehavior = LinkBehavior.HoverUnderline;
                        versionLabel.Tag = htmlUrl;
                    }));
                    Logger.Write($"[INFO] Update available: {latestTag}");
                }
                else
                {
                    BeginInvoke(new Action(() => { versionLabel.Text = $"v{CurrentVersion} — Latest"; }));
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"[WARN] Update check failed: {ex.Message}");
                BeginInvoke(new Action(() => { versionLabel.Text = $"v{CurrentVersion}"; }));
            }
        }

        private void SaveLastUpdateCheck()
        {
            try
            {
                // Reuse SaveConfig to avoid data loss — it already merges previous values.
                SaveConfig();   // This will rewrite last_update_check via the merge logic inside SaveConfig.
                                // But we need to add it explicitly.
                var config = new Dictionary<string, object>();
                if (File.Exists(configFile))
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(configFile));
                    if (existing != null)
                    {
                        foreach (var kvp in existing)
                        {
                            if (kvp.Key == "last_update_check") continue;
                            config[kvp.Key] = kvp.Value;
                        }
                    }
                }
                config["last_update_check"] = DateTime.UtcNow.ToString("O");
                File.WriteAllText(configFile, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Logger.Write($"[WARN] SaveLastUpdateCheck failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Link handlers
        // ------------------------------------------------------------------
        private void GithubLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
            => Process.Start(new ProcessStartInfo(GitHubRepoUrl) { UseShellExecute = true });

        private void AuthorLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
            => Process.Start(new ProcessStartInfo(CharacterZkillUrl) { UseShellExecute = true });

        private void VersionLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (versionLabel.Tag is string url && !string.IsNullOrEmpty(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        // ------------------------------------------------------------------
        // WinAPI declarations
        // ------------------------------------------------------------------
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
    }
}