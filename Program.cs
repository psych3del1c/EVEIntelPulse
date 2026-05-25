// EVE IntelPulse
// Copyright (c) 2026 Arnold Olympus
// Licensed under GPL v3. See LICENSE file in the repository.

using System;
using System.Windows.Forms;

namespace EVEIntelPulse
{
    internal static class Program
    {
        [STAThread]
        static void Main()
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
    }
}