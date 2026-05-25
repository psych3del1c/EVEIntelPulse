using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EVEIntelPulse
{
    /// <summary>
    /// Installs a global low-level keyboard hook to detect user-defined hotkeys
    /// regardless of which application has focus.
    /// </summary>
    public static class KeyboardHook
    {
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static string _registeredHotkey = "ctrl+shift+z";
        private static Action? _callback;

        /// <summary>
        /// Sets the hotkey string (e.g. "ctrl+shift+z") and the callback to invoke
        /// when the hotkey is pressed.
        /// </summary>
        public static void SetHotkeyAndCallback(string hotkey, Action callback)
        {
            _registeredHotkey = NormalizeHotkey(hotkey ?? "ctrl+shift+z");
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

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Both WM_KEYDOWN (standard keys) and WM_SYSKEYDOWN (system keys, e.g. Alt+Q)
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = kb.vkCode;

                bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                var parts = new List<string>();
                if (ctrl) parts.Add("ctrl");
                if (shift) parts.Add("shift");
                if (alt) parts.Add("alt");

                string keyName = ((Keys)vkCode).ToString().ToLowerInvariant();
                if (!string.IsNullOrEmpty(keyName))
                    parts.Add(keyName);

                string pressedCombo = string.Join("+", parts);
                if (pressedCombo == _registeredHotkey)
                {
                    Logger.Write($"[INFO] Hotkey pressed: {pressedCombo}");
                    _callback?.Invoke();
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

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
        [DllImport("user32.dll")]
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
    }
}