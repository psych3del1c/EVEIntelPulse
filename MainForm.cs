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
        private const string CurrentVersion = "1.0.1";               // Public release version
        private const string GitHubRepoUrl = "https://github.com/psych3del1c/EVE-IntelPulse";
        private const string UpdateCheckUrl = "https://api.github.com/repos/psych3del1c/EVE-IntelPulse/releases/latest";
        private const string CharacterZkillUrl = "https://zkillboard.com/character/92604044/";

        // Default hotkey
        private const string DefaultHotkey = "alt+q";

        // Timing values (milliseconds)
        private const int ManualModeDelayMs = 150;
        private const int ModifierReleaseTimeoutMs = 1500;           // Max wait for Alt/Ctrl/Shift to be released
        private const int ClipboardPollIntervalMs = 20;              // Clipboard polling step instead of a fixed sleep
        private const int ClipboardTimeoutMs = 1200;                 // Max wait for EVE to put the text into the clipboard
        private const int CopyAttempts = 2;
        private const int CtrlCStepMs = 15;                          // Pause between Ctrl down, C down, etc.
        private const int CtrlCHoldMs = 40;                          // How long the C key is held down
        private const int UpdateCheckIntervalHours = 6;
        private const int EveWindowRestoreDelayMs = 60;
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
        private int processingGuard;   // 0 = idle, 1 = hotkey action in progress

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
            hotkeyEntry!.Text = characterHotkey;

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
        private void HotkeyEntry_Leave(object? sender, EventArgs e)
        {
            string typed = hotkeyEntry!.Text.Trim().ToLower();

            // An empty field must never erase the hotkey: put the current value back.
            if (string.IsNullOrWhiteSpace(typed))
            {
                hotkeyEntry.Text = characterHotkey;
                return;
            }

            characterHotkey = typed;
        }

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

            // Never register an empty hotkey — keep showing the current one instead.
            if (string.IsNullOrWhiteSpace(newHotkey))
            {
                hotkeyEntry.Text = characterHotkey;
                Logger.Write("[WARN] Empty hotkey ignored");
                return;
            }

            try
            {
                characterHotkey = newHotkey;
                hotkeyEntry.Text = characterHotkey;
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

                // Older builds could save an empty value when the field was cleared.
                if (string.IsNullOrWhiteSpace(characterHotkey))
                    characterHotkey = DefaultHotkey;

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
            try
            {
                // The hook fires from a background thread; marshal to the UI (STA) thread,
                // because the WinForms Clipboard API requires an STA thread.
                BeginInvoke(new Action(() => _ = OpenCharacterLogicAsync()));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // ------------------------------------------------------------------
        // Core logic (clipboard + zkillboard)
        // ------------------------------------------------------------------
        private async Task<string> TryGetClipboardTextAsync(int attempts = 3, int delayMs = 30)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
                catch (Exception ex)
                {
                    Logger.Write($"[WARN] Clipboard read attempt {i + 1}/{attempts} failed: {ex.Message}");
                }

                if (i < attempts - 1)
                    await Task.Delay(delayMs);
            }
            return "";
        }

        private static async Task<bool> TryClearClipboardAsync()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.Clear();
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Write($"[WARN] Clipboard.Clear attempt {i + 1}/3 failed: {ex.Message}");
                    await Task.Delay(25);
                }
            }
            return false;
        }

        /// <summary>
        /// Waits until the clipboard is actually updated by the game instead of
        /// sleeping for a fixed amount of time. Polling the clipboard sequence number
        /// makes the hotkey feel instant on both Windows 10 and Windows 11.
        /// </summary>
        private static async Task<string> WaitForClipboardTextAsync(uint initialSequence, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (GetClipboardSequenceNumber() != initialSequence)
                {
                    try
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text))
                        {
                            Logger.Write($"[INFO] Clipboard updated after {sw.ElapsedMilliseconds} ms");
                            return text;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write($"[WARN] Clipboard read after change failed: {ex.Message}");
                    }
                }
                await Task.Delay(ClipboardPollIntervalMs);
            }
            return "";
        }

        private static async Task<bool> WaitForModifiersReleasedAsync(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!AnyModifierDown())
                    return true;
                await Task.Delay(10);
            }
            return !AnyModifierDown();
        }

        private static bool AnyModifierDown() =>
            IsKeyDown(VK_SHIFT) || IsKeyDown(VK_CONTROL) || IsKeyDown(VK_MENU) ||
            IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

        private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>
        /// Clears the clipboard, sends Ctrl+C to the game and waits for the result.
        /// The method retries once if the game did not copy anything.
        /// </summary>
        private async Task<string> CopySelectionAsync()
        {
            for (int attempt = 1; attempt <= CopyAttempts; attempt++)
            {
                IntPtr eveWindow = await GetActiveEveWindowOrActivateAsync();
                if (eveWindow == IntPtr.Zero)
                {
                    Logger.Write("[WARN] EVE window not found");
                    return "";
                }

                IntPtr foreground = GetForegroundWindow();
                if (foreground != eveWindow && !IsEveWindow(foreground))
                {
                    Logger.Write($"[WARN] EVE is not in the foreground (0x{foreground.ToInt64():X}), not sending Ctrl+C");
                    return "";
                }

                if (!await TryClearClipboardAsync())
                {
                    Logger.Write("[WARN] Clipboard is locked by another application");
                    continue;
                }

                // Crucial for Alt-based hotkeys: if Alt is still physically held, the game
                // receives Alt+Ctrl+C instead of Ctrl+C and never copies the selection.
                if (!await WaitForModifiersReleasedAsync(ModifierReleaseTimeoutMs))
                    Logger.Write("[WARN] Modifiers still held after timeout, sending Ctrl+C anyway");

                // Sample the clipboard state right before injecting keys, so foreign
                // clipboard updates during the wait above are not mistaken for a copy.
                uint sequence = GetClipboardSequenceNumber();

                Logger.Write($"[INFO] Copy attempt {attempt}/{CopyAttempts} -> Ctrl+C to '{GetWindowTitle(eveWindow)}'");

                if (!await SendCtrlCAsync())
                    Logger.Write($"[ERROR] SendInput failed (error {Marshal.GetLastWin32Error()})");

                string text = await WaitForClipboardTextAsync(sequence, ClipboardTimeoutMs);
                if (!string.IsNullOrEmpty(text))
                    return text;

                Logger.Write($"[WARN] Clipboard stayed empty for {ClipboardTimeoutMs} ms (attempt {attempt}/{CopyAttempts})");
            }

            return "";
        }

        private async Task OpenCharacterLogicAsync()
        {
            if (Interlocked.Exchange(ref processingGuard, 1) == 1)
            {
                Logger.Write("[INFO] Previous hotkey action is still running, ignoring this press");
                return;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                Logger.Write($"[INFO] OpenCharacterLogic start (manual mode = {manualCopyMode})");

                string rawText;
                if (!manualCopyMode)
                {
                    rawText = await CopySelectionAsync();
                }
                else
                {
                    Logger.Write("[INFO] Manual mode: reading pre-copied text");
                    await Task.Delay(ManualModeDelayMs);
                    rawText = await TryGetClipboardTextAsync();
                }

                string name = string.IsNullOrEmpty(rawText) ? "" : rawText.Trim().Replace("*", "").Trim();
                if (name.Length > 90)
                    name = name[..90];

                Logger.Write($"[INFO] Clipboard returned: '{rawText}' -> name: '{name}' ({sw.ElapsedMilliseconds} ms)");

                if (name.Length < 2)
                {
                    Logger.Write("[WARN] Clipboard empty or too short");
                    NotifyCopyFailed();
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

                Logger.Write($"[INFO] Opening browser: {url} ({sw.ElapsedMilliseconds} ms)");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] OpenCharacterLogic failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref processingGuard, 0);
            }
        }

        /// <summary>
        /// Shows a tray balloon when the selection could not be copied, so the user
        /// does not have to guess why nothing happened.
        /// </summary>
        private void NotifyCopyFailed()
        {
            try
            {
                if (trayIcon != null && trayIcon.Visible)
                {
                    trayIcon.BalloonTipTitle = "EVE IntelPulse";
                    trayIcon.BalloonTipText = "The selection was not copied. Try again, or enable Manual copy mode. If EVE runs as administrator, run this tool as administrator too.";
                    trayIcon.ShowBalloonTip(3000);
                }
            }
            catch
            {
                // Never let a notification failure break the hotkey flow.
            }
        }

        // ------------------------------------------------------------------
        // EVE window helpers
        // ------------------------------------------------------------------
        /// <summary>
        /// Returns the EVE window handle, restoring and focusing it if needed.
        /// Windows 11 is stricter about foreground changes than Windows 10, so we
        /// verify the result and retry with the AttachThreadInput trick.
        /// </summary>
        private static async Task<IntPtr> GetActiveEveWindowOrActivateAsync()
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsEveWindow(foreground))
                return foreground;

            IntPtr found = FindEveWindow();
            if (found == IntPtr.Zero)
                return IntPtr.Zero;

            if (IsIconic(found))
                ShowWindow(found, SW_RESTORE);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (ForceForeground(found))
                {
                    await Task.Delay(EveWindowRestoreDelayMs);
                    return found;
                }
                await Task.Delay(80);
            }

            Logger.Write("[WARN] Failed to bring the EVE window to the foreground");
            return IntPtr.Zero;
        }

        private static IntPtr FindEveWindow()
        {
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
            return found;
        }

        private static bool ForceForeground(IntPtr hwnd)
        {
            if (GetForegroundWindow() == hwnd)
                return true;

            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = foreground != IntPtr.Zero ? GetWindowThreadProcessId(foreground, out _) : 0;
            uint currentThread = GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(foregroundThread, currentThread, true);

                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(foregroundThread, currentThread, false);
            }

            return GetForegroundWindow() == hwnd;
        }

        private static bool IsEveWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            string title = GetWindowTitle(hwnd);
            if (title.StartsWith("eve -", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("eve online", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("eve client", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" - eve", StringComparison.OrdinalIgnoreCase))
                return true;

            // The EVE client process is called exefile.exe; this also covers
            // custom window titles.
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0)
                    return false;

                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName.Equals("exefile", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Sends Ctrl+C through SendInput, which is better supported by games than
        /// the legacy keybd_event API.
        /// </summary>
        private static async Task<bool> SendCtrlCAsync()
        {
            bool ctrlDown = SendKey(VK_CONTROL, keyUp: false);
            await Task.Delay(CtrlCStepMs);
            bool cDown = SendKey(VK_C, keyUp: false);
            await Task.Delay(CtrlCHoldMs);
            bool cUp = SendKey(VK_C, keyUp: true);
            await Task.Delay(CtrlCStepMs);
            bool ctrlUp = SendKey(VK_CONTROL, keyUp: true);
            return ctrlDown && cDown && cUp && ctrlUp;
        }

        private static bool SendKey(int vk, bool keyUp)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)vk,
                        wScan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC),
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                    }
                }
            };

            return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
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
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const int SW_RESTORE = 9;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_C = 0x43;
    }
}