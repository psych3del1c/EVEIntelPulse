// EVE IntelPulse
// Copyright (c) 2026 Arnold Olympus
// Licensed under GPL v3. See LICENSE file in the repository.

#nullable disable

using System;
using System.Drawing;
using System.Windows.Forms;

namespace EVEIntelPulse
{
    partial class MainForm
    {
        private GroupBox hotkeyGroup;
        private Label hotkeyLabel;
        private TextBox hotkeyEntry;
        private Button applyBtn;
        private CheckBox manualCopyCheckBox;
        private CheckBox startMinimizedCheckBox;
        private Button trayBtn;
        private Button exitBtn;
        private TableLayoutPanel footerLayout;
        private Label authorPrefixLabel;
        private LinkLabel authorLinkLabel;
        private Label authorSuffixLabel;
        private LinkLabel githubLinkLabel;
        private LinkLabel versionLabel;
        private FlowLayoutPanel topLine;

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new Size(320, 320);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "EVE IntelPulse";
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.BackColor = Color.FromArgb(245, 245, 245);

            hotkeyGroup = new GroupBox
            {
                Text = "Hotkey Settings",
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                Size = new Size(280, 195),
                Location = new Point((320 - 280) / 2, 15),
                TabIndex = 0,
                TabStop = false
            };

            hotkeyLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(70, 70, 70),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(240, 23),
                Location = new Point((280 - 240) / 2, 25)
            };
            hotkeyGroup.Controls.Add(hotkeyLabel);

            hotkeyEntry = new TextBox
            {
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                MaxLength = 50,
                PlaceholderText = "Type hotkey combination...",
                Size = new Size(180, 23),
                Location = new Point((280 - 180) / 2, 55),
                TextAlign = HorizontalAlignment.Center
            };
            hotkeyEntry.Leave += HotkeyEntry_Leave;
            hotkeyGroup.Controls.Add(hotkeyEntry);

            applyBtn = new Button
            {
                Text = "Apply",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Size = new Size(100, 30),
                Location = new Point((280 - 100) / 2, 90),
                Cursor = Cursors.Hand
            };
            applyBtn.FlatAppearance.BorderSize = 0;
            applyBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 100, 180);
            applyBtn.Click += ApplyHotkey_Click;
            hotkeyGroup.Controls.Add(applyBtn);

            manualCopyCheckBox = new CheckBox
            {
                Text = "Manual copy mode",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                AutoSize = true,
                Location = new Point(20, 130),
                Checked = false
            };
            manualCopyCheckBox.CheckedChanged += ManualCopyCheckBox_CheckedChanged;
            hotkeyGroup.Controls.Add(manualCopyCheckBox);

            startMinimizedCheckBox = new CheckBox
            {
                Text = "Start minimized",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                AutoSize = true,
                Location = new Point(20, 155),
                Checked = false
            };
            startMinimizedCheckBox.CheckedChanged += StartMinimizedCheckBox_CheckedChanged;
            hotkeyGroup.Controls.Add(startMinimizedCheckBox);

            trayBtn = new Button
            {
                Text = "Minimize to Tray",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 220, 220),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Size = new Size(130, 30),
                Location = new Point(25, 225),
                Cursor = Cursors.Hand
            };
            trayBtn.FlatAppearance.BorderSize = 1;
            trayBtn.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            trayBtn.Click += TrayBtn_Click;

            exitBtn = new Button
            {
                Text = "Exit",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 50, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Size = new Size(130, 30),
                Location = new Point(165, 225),
                Cursor = Cursors.Hand
            };
            exitBtn.FlatAppearance.BorderSize = 0;
            exitBtn.Click += ExitBtn_Click;

            // Footer – three rows: "by", author line, version
            footerLayout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.Gray,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            footerLayout.RowStyles.Clear();
            footerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            authorPrefixLabel = new Label
            {
                Text = "by",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            authorLinkLabel = new LinkLabel
            {
                Text = "Arnold Olympus",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                LinkColor = Color.FromArgb(100, 149, 237),
                ActiveLinkColor = Color.FromArgb(100, 149, 237),
                VisitedLinkColor = Color.FromArgb(100, 149, 237),
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            authorLinkLabel.LinkClicked += AuthorLinkLabel_LinkClicked;

            authorSuffixLabel = new Label
            {
                Text = " ·    Sognare    | ",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            githubLinkLabel = new LinkLabel
            {
                Text = "GitHub",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                LinkColor = Color.FromArgb(100, 149, 237),
                ActiveLinkColor = Color.FromArgb(100, 149, 237),
                VisitedLinkColor = Color.FromArgb(100, 149, 237),
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            githubLinkLabel.LinkClicked += GithubLinkLabel_LinkClicked;

            topLine = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            topLine.Controls.Add(authorLinkLabel);
            topLine.Controls.Add(authorSuffixLabel);
            topLine.Controls.Add(githubLinkLabel);

            // Row 2: version
            versionLabel = new LinkLabel
            {
                Text = "v" + CurrentVersion,
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                LinkColor = Color.Gray,
                ActiveLinkColor = Color.Gray,
                VisitedLinkColor = Color.Gray,
                LinkBehavior = LinkBehavior.NeverUnderline,
                Anchor = AnchorStyles.None
            };
            versionLabel.LinkClicked += VersionLabel_LinkClicked;

            footerLayout.Controls.Add(authorPrefixLabel, 0, 0);
            footerLayout.Controls.Add(topLine, 0, 1);
            footerLayout.Controls.Add(versionLabel, 0, 2);

            this.Controls.Add(hotkeyGroup);
            this.Controls.Add(trayBtn);
            this.Controls.Add(exitBtn);
            this.Controls.Add(footerLayout);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}