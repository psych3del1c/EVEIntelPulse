using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EVEIntelPulse
{
    /// <summary>
    /// Installs a global low-level keyboard hook to detect user-defined hotkeys
    /// regardless of which application has focus.
    /// The callback is fired only after the key and all modifiers are physically
    /// released, so the application never injects Ctrl+C while Alt is still held
    /// (Alt+Ctrl+C is not a copy command for games).
    /// </summary>
    public static class KeyboardHook
    {
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static string _registeredHotkey = "alt+q";
        private static Action? _callback;

        // How long we wait for the user to release the hotkey before firing anyway.
        private const int ModifierReleaseTimeoutMs = 1500;
        private const int PollIntervalMs = 8;
        private const int TriggerDebounceMs = 250;

        // Guards and state shared between the hook thread and the release worker.
        private static int _releaseWorkerRunning;
        private static long _lastTriggerTicks;

        /// <summary>
        /// Sets the hotkey string (e.g. "ctrl+shift+z") and the callback to invoke
        /// when the hotkey is pressed and released.
        /// </summary>
        public static void SetHotkeyAndCallback(string hotkey, Action callback)
        {
            _registeredHotkey = NormalizeHotkey(hotkey ?? "alt+q");
            _callback = callback;
            Logger.Write($"[INFO] Keyboard hook registered: '{_registeredHotkey}'");
        }

        /// <summary>
        /// Normalizes a user-supplied hotkey string into a canonical format.
        /// For example "control + shift + z" becomes "ctrl+shift+z".
        /// </summary>
        private static string NormalizeHotkey(string raw)
        {
            var parts = raw.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(p => p.Trim().ToLowerInvariant())
                           .ToList();

            var modifiers = new List<string>();
            string key = "";

            foreach (var p in parts)
            {
                if (p == "ctrl" || p == "control") modifiers.Add("ctrl");
                else if (p == "shift") modifiers.Add("shift");
                else if (p == "alt" || p == "menu") modifiers.Add("alt");
                else key = p;
            }

            modifiers = modifiers.Distinct().OrderBy(m => m switch { "ctrl" => 1, "shift" => 2, "alt" => 3, _ => 99 }).ToList();

            if (modifiers.Count > 0 && !string.IsNullOrEmpty(key))
                return string.Join("+", modifiers) + "+" + key;
            if (modifiers.Count > 0)
                return string.Join("+", modifiers);
            return key;
        }

        /// <summary>
        /// Installs the global keyboard hook. Should be called once at startup.
        /// </summary>
        public static void InstallHook()
        {
            UnhookAll();
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                GetModuleHandle(curModule!.ModuleName), 0);

            if (_hookID == IntPtr.Zero)
                Logger.Write($"[ERROR] Keyboard hook installation failed (error {Marshal.GetLastWin32Error()})");
            else
                Logger.Write($"[INFO] Keyboard hook installed (ID: {_hookID})");
        }

        /// <summary>
        /// Removes the keyboard hook if it was installed.
        /// </summary>
        public static void UnhookAll()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                Logger.Write("[INFO] Keyboard hook uninstalled");
                _hookID = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Hook callback. It must return as fast as possible: the OS silently drops
        /// low-level hooks whose callback blocks for too long, so all waiting is
        /// moved to a background worker.
        /// </summary>
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Both WM_KEYDOWN (standard keys) and WM_SYSKEYDOWN (system keys, e.g. Alt+Q)
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = kb.vkCode;

                if (BuildComboString(vkCode) == _registeredHotkey)
                {
                    if (Interlocked.Exchange(ref _releaseWorkerRunning, 1) == 0)
                    {
                        var callback = _callback;
                        Task.Run(() => WaitForReleaseAndFireAsync(vkCode, callback));
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        /// <summary>
        /// Waits until the hotkey key and every modifier are released, then fires the
        /// callback once. This is the core fix for Alt-based hotkeys: the simulated
        /// Ctrl+C must not overlap with a physically held Alt.
        /// </summary>
        private static async Task WaitForReleaseAndFireAsync(int vkCode, Action? callback)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < ModifierReleaseTimeoutMs)
                {
                    if (!IsKeyDown(vkCode) && !AnyModifierDown())
                        break;
                    await Task.Delay(PollIntervalMs).ConfigureAwait(false);
                }

                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _lastTriggerTicks) < TriggerDebounceMs)
                    return;

                Interlocked.Exchange(ref _lastTriggerTicks, now);
                Logger.Write($"[INFO] Hotkey fired: {_registeredHotkey} (released after {sw.ElapsedMilliseconds} ms)");
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Write($"[ERROR] Hotkey handler failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _releaseWorkerRunning, 0);
            }
        }

        private static string BuildComboString(int vkCode)
        {
            var parts = new List<string>();
            if (IsKeyDown(VK_CONTROL)) parts.Add("ctrl");
            if (IsKeyDown(VK_SHIFT)) parts.Add("shift");
            if (IsKeyDown(VK_MENU)) parts.Add("alt");

            string keyName = ((Keys)vkCode).ToString().ToLowerInvariant();
            if (!string.IsNullOrEmpty(keyName))
                parts.Add(keyName);

            return string.Join("+", parts);
        }

        private static bool AnyModifierDown() =>
            IsKeyDown(VK_CONTROL) || IsKeyDown(VK_SHIFT) || IsKeyDown(VK_MENU) ||
            IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

        private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        // P/Invoke
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
    }
}