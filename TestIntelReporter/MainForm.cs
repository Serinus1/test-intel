﻿using Microsoft.Win32;
using PleaseIgnore.IntelMap;
using System;
using System.ComponentModel;
using System.Security;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using System.Diagnostics;
using TestIntelReporter.Properties;
using System.Net;
using System.Security.Authentication;
using System.Collections.Generic;

namespace TestIntelReporter {
    public partial class MainForm : Form {
        // Pen used for drawing the panel borders
        private readonly Pen borderPen = new Pen(Color.Gray, 4.0f);
        // The format string for labelCounts
        private readonly string formatCounts;
        // The settings object
        private readonly Settings settings;
        // List of channel status flags
        private readonly List<Label> channelFlags = new List<Label>();
        // List of channel upload counts
        private readonly List<Label> channelCounts = new List<Label>();

        // Number of times DaBigRedBoat has shown up in a message
        private int noveltyCount;
        // The most recently reported forum status
        private IntelStatus oldStatus;
        // The configuration is incomplete
        private bool configError;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MainForm"/> class.
        /// </summary>
        public MainForm() {
            InitializeComponent();

            // Claim some fields
            formatCounts = labelCounts.Text;
            // Update some fields
            this.Icon = Properties.Resources.AppIcon;
            labelAppName.Text = string.Format(
                Application.CurrentCulture,
                labelAppName.Text,
                Application.ProductVersion);
            // Finish initialization
            this.settings = Settings.Default;
            if (!String.IsNullOrEmpty(settings.Username)) {
                intelReporter.Username = settings.Username;
            } else {
                this.configError = true;
            }
            if (!String.IsNullOrEmpty(settings.PasswordHash)) {
                intelReporter.PasswordHash = settings.PasswordHash;
            } else {
                this.configError = true;
            }
        }

        /// <summary>
        ///     Starts up the intel reporting component.
        /// </summary>
        protected override void OnShown(EventArgs e) {
            intelReporter.Start();
            this.UpdateStatus();
            base.OnShown(e);
        }

        /// <summary>
        ///     Actives the specified pseudo-dialog within the parent window.
        ///     The operation includes centering, z-order manipulation, and
        ///     focus changes.
        /// </summary>
        private void ShowPanel(Control panel, Control focus) {
            // Recenter and show a child panel
            var parent = panel.Parent;
            if (parent != null) {
                var margin = parent.ClientSize - panel.Size;
                margin.Height /= 2;
                margin.Width /= 2;
                panel.Location = parent.ClientRectangle.Location + margin;
            }
            panel.BringToFront();
            panel.Visible = true;
            (focus ?? panel).Focus();
        }

        /// <summary>
        ///     Draws a border for the pseudo-dialogs.
        /// </summary>
        private void DrawBorder(object sender, PaintEventArgs e) {
            var panel = (Control)sender;
            var rect = panel.ClientRectangle;
            e.Graphics.DrawRectangle(borderPen,
                rect.Left + 0.5f * borderPen.Width,
                rect.Top + 0.5f * borderPen.Width,
                rect.Width - borderPen.Width,
                rect.Height - borderPen.Width);
        }

        #region Status Reporting
        /// <summary>
        ///     Displays the status pseudo-dialog.
        /// </summary>
        private void ShowStatus(string title, string message) {
            if (!panelAuthentication.Visible) {
                panelStatus.Visible = false;
                panelAuthError.Visible = false;
                panelChannels.Visible = false;
                labelStatusTitle.Text = title;
                labelStatus.Text = message;
                this.ShowPanel(this.panelStatus, null);
            }
        }

        /// <summary>
        ///     Displays the channel list pseudo-dialog.
        /// </summary>
        private void ShowChannelList() {
            if (!panelChannels.Visible) {
                panelStatus.Visible = false;
                panelAuthError.Visible = false;
                panelChannels.Visible = false;
                this.ShowPanel(this.panelChannels, null);
            }
        }

        /// <summary>
        ///     Performs an UI manipulations required for GUI depicted status.
        /// </summary>
        private void UpdateStatus() {
            // Simple updates
            labelCounts.Text = string.Format(
                Application.CurrentCulture,
                formatCounts,
                intelReporter.IntelSent,
                intelReporter.IntelDropped,
                this.noveltyCount);

            // Authentication errors are handled specially
            if (this.configError && !panelAuthentication.Visible) {
                this.ShowAuthWindow();
            }

            // Status changes
            var status = intelReporter.Status;
            if (this.oldStatus != status) {
                this.oldStatus = status;
                switch (status) {
                case IntelStatus.Connected:
                    // Normal operation
                    this.ShowChannelList();
                    break;
                case IntelStatus.Idle:
                    // Normal operation
                    this.ShowStatus(
                        Resources.IntelStatus_IdleTitle,
                        Resources.IntelStatus_Idle);
                    break;
                case IntelStatus.AuthenticationFailure:
                    // Doesn't like our password
                    if (!this.configError) {
                        this.ShowAuthError(
                            Resources.IntelStatus_AuthTitle,
                            Resources.IntelStatus_Auth);
                    }
                    break;
                case IntelStatus.MissingDirectory:
                    // Couldn't find the log directory
                    this.ShowStatus(
                        Resources.IntelStatus_MissingTitle,
                        Resources.IntelStatus_Missing);
                    break;
                case IntelStatus.NetworkError:
                    // Unable to contact the network server
                    this.ShowStatus(
                        Resources.IntelStatus_ErrorTitle,
                        Resources.IntelStatus_Error);
                    break;
                case IntelStatus.Initializing:
                case IntelStatus.Stopped:
                case IntelStatus.FatalError:
                    // This represents a pretty critical failure of the
                    // system, so it gets priority over everything, including
                    // user entry.
                    panelAuthentication.Visible = false;
                    this.ShowStatus(
                        Resources.IntelStatus_FatalTitle,
                        Resources.IntelStatus_Fatal);
                    break;
                }
            }
        }

        /// <summary>
        ///     Sent by <see cref="IntelReporter"/> every time it parses a
        ///     new log entry.
        /// </summary>
        private void intelReporter_IntelReported(object sender, IntelEventArgs e) {
            messageView.PushMessage(e);
            if (e.Message.IndexOf("Dabigredboat", StringComparison.OrdinalIgnoreCase) != -1) {
                ++this.noveltyCount;
                this.UpdateStatus();
            }
        }

        /// <summary>
        ///     Sent by <see cref="IntelReporter"/> whenever there is some
        ///     sort of status change.
        /// </summary>
        private void intelReporter_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            this.UpdateStatus();

            // Update the channel list
            if (String.IsNullOrEmpty(e.PropertyName) || (e.PropertyName == "Channels")) {
                var visible = panelChannels.Visible;
                // Clear out the old channel list
                foreach (var label in panelChannels.Controls
                        .OfType<Label>()
                        .Where(x => x.Tag != null)
                        .ToArray()) {
                    panelChannels.Controls.Remove(label);
                    label.Dispose();
                }
                channelFlags.Clear();
                channelCounts.Clear();

                // Clear out the old rows
                while(panelChannels.RowStyles.Count > 1) {
                    panelChannels.RowStyles.RemoveAt(1);
                }

                // Create a new channel list
                foreach (var channel in intelReporter.Channels
                        .OrderBy(x => x.Name)) {
                    var row = panelChannels.RowStyles.Count;
                    panelChannels.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                    var status = new Label {
                        Tag = channel,
                        ImageList = this.imageList,
                        Padding = new Padding(0)
                    };
                    panelChannels.Controls.Add(status, 0, row);
                    channelFlags.Add(status);

                    var name = new Label {
                        Text = channel.Name,
                        Tag = channel,
                        Padding = new Padding(0),
                        Anchor = AnchorStyles.Left | AnchorStyles.Right
                    };
                    panelChannels.Controls.Add(name, 1, row);

                    var count = new Label {
                        Text = String.Empty,
                        Tag = channel,
                        Padding = new Padding(0),
                        TextAlign = ContentAlignment.MiddleRight,
                        Anchor = AnchorStyles.Left | AnchorStyles.Right
                    };
                    panelChannels.Controls.Add(count, 2, row);
                    channelCounts.Add(count);
                }

                if (visible) {
                    // Will recenter the channel list
                    this.ShowPanel(this.panelChannels, null);
                }
            }

            // Update the channel statistics
            foreach (var flag in channelFlags) {
                var channel = (IntelChannel)flag.Tag;
                flag.ImageIndex = (channel.LogFile != null) ? 0 : 1;
            }
            foreach (var label in channelCounts) {
                var channel = (IntelChannel)label.Tag;
                var count = channel.IntelCount;
                switch(count) {
                case 0:
                    label.Text = string.Format(
                        Application.CurrentCulture,
                        Resources.IntelCount_Zero,
                        count);
                    break;
                case 1:
                    label.Text = string.Format(
                        Application.CurrentCulture,
                        Resources.IntelCount_One,
                        count);
                    break;
                default:
                    label.Text = string.Format(
                        Application.CurrentCulture,
                        Resources.IntelCount_Many,
                        count);
                    break;
                }
            }
        }
        #endregion

        #region Authentication Handling
        /// <summary>
        ///     Displays the authentication error dialog (includes a button).
        /// </summary>
        private void ShowAuthError(string title, string message) {
            panelStatus.Visible = false;
            panelAuthError.Visible = false;
            panelChannels.Visible = false;
            labelAuthErrorTitle.Text = title;
            labelAuthError.Text = message;
            this.ShowPanel(this.panelAuthError, this.buttonChangeAuth);
            this.AcceptButton = this.buttonChangeAuth;
        }

        /// <summary>
        ///     Displays the login window.
        /// </summary>
        private void ShowAuthWindow() {
            panelStatus.Visible = false;
            panelAuthError.Visible = false;
            textBoxUsername.Text = intelReporter.Username;
            textBoxPassword.Text = String.Empty;
            this.textBoxAuth_TextChanged(null, EventArgs.Empty);
            ShowPanel(this.panelAuthentication, this.textBoxUsername);
            this.AcceptButton = this.buttonLogin;
        }

        /// <summary>
        ///     Reevaluate the validity of the authentication inputs and
        ///     adjust the controls to reflect that.
        /// </summary>
        private void textBoxAuth_TextChanged(object sender, EventArgs e) {
            bool authValid = true;
            if (!String.IsNullOrWhiteSpace(textBoxUsername.Text)) {
                textBoxUsername.BackColor = Color.White;
            } else {
                textBoxUsername.BackColor = Color.Pink;
                authValid = false;
            }

            if (!String.IsNullOrWhiteSpace(textBoxPassword.Text)) {
                textBoxPassword.BackColor = Color.White;
            } else {
                textBoxPassword.BackColor = Color.Pink;
                authValid = false;
            }

            buttonLogin.Enabled = authValid;
        }

        /// <summary>
        ///     Login button has been clicked.  Begin an authentication
        ///     attempt.
        /// </summary>
        private void buttonLogin_Click(object sender, EventArgs e) {
            // TODO: Show some sort of status dialog
            textBoxUsername.Enabled = false;
            textBoxPassword.Enabled = false;
            buttonLogin.Enabled = false;

            intelReporter.BeginAuthenticate(
                textBoxUsername.Text.Trim(),
                textBoxPassword.Text.Trim(),
                intelReporter_onAuthenticateComplete,
                null);
        }

        /// <summary>
        ///     Login attempt has completed.  Display the results.
        /// </summary>
        private void intelReporter_onAuthenticateComplete(IAsyncResult asyncResult) {
            // TODO: Let IntelReporter handle the invoking for us
            if (this.InvokeRequired) {
                this.BeginInvoke(
                    new AsyncCallback(intelReporter_onAuthenticateComplete),
                    asyncResult);
                return;
            }

            // Hide the authentication dialog if it was successful; otherwise,
            // display the appropriate error.
            try {
                intelReporter.EndAuthenticate(asyncResult);
                panelAuthentication.Visible = false;
                
                this.configError = false;
                settings.Username = intelReporter.Username;
                settings.PasswordHash = intelReporter.PasswordHash;
                settings.Save();
            } catch (WebException) {
                this.ShowAuthError(
                    Resources.IntelStatus_ErrorTitle,
                    Resources.Authenticate_Network);
            } catch (IntelException) {
                this.ShowAuthError(
                    Resources.IntelStatus_ErrorTitle,
                    Resources.Authenticate_Network);
            } catch (AuthenticationException) {
                this.ShowAuthError(
                    Resources.IntelStatus_AuthTitle,
                    Resources.Authenticate_Auth);
            }
        }

        /// <summary>
        ///     Clicked da butan in an authentication error dialog.
        /// </summary>
        private void buttonChangeAuth_Click(object sender, EventArgs e) {
            if (panelAuthentication.Visible) {
                // Maintain the current login
                panelAuthError.Visible = false;
                textBoxUsername.Enabled = true;
                textBoxPassword.Enabled = true;
                this.AcceptButton = this.buttonLogin;
            } else {
                // Open a new login window
                this.ShowAuthWindow();
            }
        }
        #endregion
    }
}
