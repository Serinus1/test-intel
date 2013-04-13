using System;
using System.Windows.Forms;

namespace TestIntelReporter {
    public partial class MainForm : Form {
        Properties.Settings settings;

        public MainForm() {
            InitializeComponent();

            // Initialize additional properties
            this.Icon = Properties.Resources.AppIcon;
            this.settings = Properties.Settings.Default;
            notifyIcon.Icon = Properties.Resources.AppIcon;
            notifyIcon.Visible = true;

            // Load the Configuration
            textUsername.Text = logWatcher.Username = settings.Username;
            if (!String.IsNullOrEmpty(settings.PasswordHash)) {
                logWatcher.PasswordHash = settings.PasswordHash;
            }
            if (!String.IsNullOrEmpty(settings.LogDirectory)) {
                logWatcher.LogDirectory = settings.LogDirectory;
            }
            textLogDirectory.Text = logWatcher.LogDirectory;
        }

        private void textUsername_TextChanged(object sender, EventArgs e) {
            buttonApply.Enabled = !string.IsNullOrWhiteSpace(textPassword.Text)
                && !string.IsNullOrWhiteSpace(textUsername.Text);
        }

        private void textPassword_TextChanged(object sender, EventArgs e) {
            textUsername_TextChanged(sender, e);
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

        private void checkAutostart_CheckedChanged(object sender, EventArgs e) {
            settings.AutoStart = checkAutostart.Checked;
            settings.Save();
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

        private void linkTestMap_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            linkTestMap.LinkVisited = true;
            System.Diagnostics.Process.Start("http://maps.pleaseignore.com/");
        }

        private void MainForm_Resize(object sender, EventArgs e) {
            switch (this.WindowState) {
            case FormWindowState.Minimized:
                this.Hide();
                break;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
            switch (e.CloseReason) {
            case CloseReason.UserClosing:
                // Hide the form if the user is trying to close us
                this.Hide();
                e.Cancel = true;
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
            Application.Exit();
        }
    }
}
