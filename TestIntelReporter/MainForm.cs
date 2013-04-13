using Microsoft.Win32;
using System;
using System.Security;
using System.Windows.Forms;

namespace TestIntelReporter {
    public partial class MainForm : Form {
        Properties.Settings settings;
        bool closing;
        private const string AutoRunKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AutoRunValue = "TestIntelReporter";

        public MainForm() {
            InitializeComponent();

            // Initialize additional properties
            this.Icon = Properties.Resources.AppIcon;
            labelAppName.Text = String.Format(labelAppVersion.Text, Application.ProductName);
            labelAppVersion.Text = String.Format(labelAppVersion.Text, Application.ProductVersion);
            notifyIcon.Icon = Properties.Resources.AppIcon;
            notifyIcon.Visible = true;

            // Load the Configuration
            this.settings = Properties.Settings.Default;
            textUsername.Text = logWatcher.Username = settings.Username;
            if (!String.IsNullOrEmpty(settings.PasswordHash)) {
                logWatcher.PasswordHash = settings.PasswordHash;
            }
            if (!String.IsNullOrEmpty(settings.LogDirectory)) {
                logWatcher.LogDirectory = settings.LogDirectory;
            }
            textLogDirectory.Text = logWatcher.LogDirectory;
            checkAutostart.Checked = settings.AutoStart;
            checkMinimizeOnClose.Checked = settings.HideOnClose;
            checkHideMinimized.Checked = settings.HideOnMinimize;
            this.UpdateAutoRun();

            // Update the status
            if (!String.IsNullOrEmpty(settings.Username)
                    && !String.IsNullOrEmpty(settings.PasswordHash)) {
                logWatcher.Start();
            }
            this.UpdateStatus();
        }

        public bool HasConfig { get { return logWatcher.IsRunning; } }

        private void UpdateAutoRun() {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoRunKey, true)) {
                    if (key != null) {
                        if (settings.AutoStart) {
                            key.SetValue(AutoRunValue,
                                '"' + Application.ExecutablePath + '"',
                                RegistryValueKind.String);
                        } else {
                            key.DeleteValue(AutoRunValue, false);
                        }
                    }
                }
            } catch(SecurityException) {
            }
        }

        private void UpdateStatus() {
            toolStripStatusIntel.Text = string.Format(
                (logWatcher.IntelReported == 1)
                    ? Properties.Resources.OneIntelReported
                    : Properties.Resources.MultiIntelReported,
                logWatcher.IntelReported);
            toolStripStatusUsers.Text = string.Format(
                (logWatcher.Users == 1)
                    ? Properties.Resources.OneUserReporting
                    : Properties.Resources.MultiUserReporting,
                logWatcher.Users);

            if (logWatcher.IsConnected) {
                toolStripStatus.Text = Properties.Resources.AppConnected;
                toolStripStatusUsers.Visible = true;
            } else if (logWatcher.BadPassword) {
                toolStripStatus.Text = Properties.Resources.AppAuthenticateFailed;
                toolStripStatusUsers.Visible = false;
            } else if (logWatcher.IsRunning) {
                toolStripStatus.Text = Properties.Resources.AppIdle;
                toolStripStatusUsers.Visible = false;
            } else {
                toolStripStatus.Text = Properties.Resources.AppConfigure;
                toolStripStatusUsers.Visible = false;
            }
        }

        private void textUsername_TextChanged(object sender, EventArgs e) {
            buttonApply.Enabled = !string.IsNullOrWhiteSpace(textPassword.Text)
                && !string.IsNullOrWhiteSpace(textUsername.Text);
        }

        private void textPassword_TextChanged(object sender, EventArgs e) {
            textUsername_TextChanged(sender, e);
        }

        private void buttonApply_Click(object sender, EventArgs e) {
            settings.Username = logWatcher.Username = textUsername.Text.Trim();
            logWatcher.PasswordHash = settings.PasswordHash
                = IntelSession.HashPassword(textPassword.Text.Trim());
            logWatcher.LogDirectory = textLogDirectory.Text;
            settings.Save();

            logWatcher.Start();
            buttonApply.Enabled = false;
            textPassword.Text = "";
        }

        private void buttonBrowse_Click(object sender, EventArgs e) {
            folderBrowserDialog.SelectedPath = textLogDirectory.Text;
            switch (folderBrowserDialog.ShowDialog(this)) {
            case DialogResult.OK:
                textLogDirectory.Text = folderBrowserDialog.SelectedPath;
                buttonApply.Enabled = true;
                break;
            }
        }

        private void checkMinimizeOnClose_CheckedChanged(object sender, EventArgs e) {
            settings.HideOnClose = checkMinimizeOnClose.Checked;
            settings.Save();
        }

        private void checkHideMinimized_CheckedChanged(object sender, EventArgs e) {
            settings.HideOnMinimize = checkHideMinimized.Checked;
            settings.Save();
        }

        private void checkAutostart_CheckedChanged(object sender, EventArgs e) {
            settings.AutoStart = checkAutostart.Checked;
            settings.Save();
            UpdateAutoRun();
        }

        private void linkTestMap_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            linkTestMap.LinkVisited = true;
            System.Diagnostics.Process.Start("http://maps.pleaseignore.com/");
        }

        private void MainForm_Resize(object sender, EventArgs e) {
            switch (this.WindowState) {
            case FormWindowState.Minimized:
                // Hide the form instead of minimizing it
                if (settings.HideOnMinimize) {
                    this.Hide();
                }
                break;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
            switch (e.CloseReason) {
            case CloseReason.UserClosing:
                // Hide the form if the user is trying to close us
                if (!closing && settings.HideOnClose) {
                    this.Hide();
                    e.Cancel = true;
                } else {
                    Application.Exit();
                }
                break;
            default:
                Application.Exit();
                break;
            }
        }

        private void notifyIcon_DoubleClick(object sender, EventArgs e) {
            this.Show();
            this.Activate();
        }

        private void configurationToolStripMenuItem_Click(object sender, EventArgs e) {
            tabControl.SelectTab(configurationPage);
            notifyIcon_DoubleClick(sender, e);
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e) {
            tabControl.SelectTab(aboutPage);
            notifyIcon_DoubleClick(sender, e);
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e) {
            closing = true;
            if (this.Created) {
                this.Close();
            } else {
                this.Dispose();
                Application.Exit();
            }
        }

        private void logWatcher_StatusChanged(object sender, EventArgs e) {
            if (!this.IsHandleCreated) {
                this.UpdateStatus();
            } else {
                this.BeginInvoke((MethodInvoker)this.UpdateStatus, null);
            }
        }

    }
}
