using Microsoft.Win32;
using PleaseIgnore.IntelMap;
using System;
using System.ComponentModel;
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
            labelAppName.Text = String.Format(labelAppName.Text, Application.ProductName);
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
            logWatcher.Start();
        }

        public bool HasConfig {
            get {
                return !String.IsNullOrEmpty(logWatcher.Username)
                    && !String.IsNullOrEmpty(logWatcher.PasswordHash);
            }
        }

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
                (logWatcher.IntelSent == 1)
                    ? Properties.Resources.OneIntelReported
                    : Properties.Resources.MultiIntelReported,
                logWatcher.IntelSent);
            toolStripStatusUsers.Text = string.Format(
                (logWatcher.Users == 1)
                    ? Properties.Resources.OneUserReporting
                    : Properties.Resources.MultiUserReporting,
                logWatcher.Users);

            switch (logWatcher.Status) {
            case IntelStatus.AuthenticationFailure:
                toolStripStatus.Text = Properties.Resources.AppAuthenticateFailed;
                toolStripStatusUsers.Visible = false;
                break;
            case IntelStatus.Connected:
                toolStripStatus.Text = Properties.Resources.AppConnected;
                toolStripStatusUsers.Visible = true;
                break;
            case IntelStatus.Idle:
                toolStripStatus.Text = Properties.Resources.AppIdle;
                toolStripStatusUsers.Visible = false;
                break;
            case IntelStatus.MissingDirectory:
                toolStripStatus.Text = Properties.Resources.AppDirectory;
                toolStripStatusUsers.Visible = false;
                break;
            default:
                toolStripStatus.Text = Properties.Resources.AppError;
                toolStripStatusUsers.Visible = false;
                break;
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
            var username = textUsername.Text.Trim();
            var password = textPassword.Text.Trim();
            
            try {
                logWatcher.Authenticate(username, password);
                settings.Username = logWatcher.Username;
                settings.PasswordHash = logWatcher.PasswordHash;
                settings.Save();
                buttonApply.Enabled = false;
            } catch (Exception) {
            }
        }

        private void buttonBrowse_Click(object sender, EventArgs e) {
            folderBrowserDialog.SelectedPath = textLogDirectory.Text;
            switch (folderBrowserDialog.ShowDialog(this)) {
            case DialogResult.OK:
                textLogDirectory.Text = folderBrowserDialog.SelectedPath;
                logWatcher.LogDirectory = folderBrowserDialog.SelectedPath;
                settings.LogDirectory = folderBrowserDialog.SelectedPath;
                settings.Save();
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

        private void logWatcher_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (!this.IsHandleCreated) {
                this.UpdateStatus();
            } else {
                this.BeginInvoke((MethodInvoker)this.UpdateStatus, null);
            }
        }

    }
}
