// EVE IntelPulse
// Copyright (c) 2026 Arnold Olympus
// Licensed under GPL v3. See LICENSE file in the repository.

using System;
using System.Threading;
using System.Windows.Forms;

namespace EVEIntelPulse
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\EVEIntelPulse_SingleInstance";

        [STAThread]
        static void Main()
        {
            // Only one instance may run: every instance installs the same global
            // hotkey, so a second copy would open a second browser tab for every press.
            using var mutex = new Mutex(false, SingleInstanceMutexName);
            bool isOnlyInstance;
            try
            {
                isOnlyInstance = mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                // The previous instance crashed without releasing the mutex.
                isOnlyInstance = true;
            }

            if (!isOnlyInstance)
            {
                MessageBox.Show(
                    "EVE IntelPulse is already running. Check the system tray.",
                    "EVE IntelPulse",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += (_, e) =>
                    MessageBox.Show(e.Exception.ToString(), @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                    MessageBox.Show(e.ExceptionObject.ToString(), @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                try
                {
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), @"Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }
}