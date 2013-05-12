using Microsoft.Win32;
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
using System.Globalization;
using System.Threading;

namespace TestIntelReporter {
    public partial class MainForm : Form {
        // Pen used for drawing the panel borders
        private readonly Pen borderPen = new Pen(Color.Gray, 4.0f);
        // The settings object
        private readonly Settings settings;
        // List of channel status flags
        private readonly List<Label> channelFlags = new List<Label>();
        // List of channel upload counts
        private readonly List<Label> channelCounts = new List<Label>();
        // Message we receive from Main() when another instance is run
        private readonly uint wakeupMessage;

        // Number of times DaBigRedBoat has shown up in a message
        private int noveltyCount;
        // The most recently reported forum status
        private IntelStatus oldStatus;
        // The configuration is incomplete
        private bool configError;
        // The most recent update notification
        private UpdateEventArgs updateEvent;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MainForm"/> class.
        /// </summary>
        public MainForm() {
            InitializeComponent();

            // Register the 'wake up' message
            try {
                this.wakeupMessage = NativeMethods.RegisterWindowMessage(Program.mutexName);
            } catch {
                this.wakeupMessage = 0;
            }

            // Update some fields
            this.Icon = Properties.Resources.AppIcon;
            labelAppName.Text = string.Format(
                Application.CurrentCulture,
                labelAppName.Text,
                Application.ProductVersion);

            // Finish initialization
            this.settings = new Settings();
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
        ///     Looking for the wake-up message.
        /// </summary>
        protected override void WndProc(ref Message m) {
            if ((this.wakeupMessage != 0) && (this.wakeupMessage == m.Msg)) {
                this.Visible = true;
                this.WindowState = FormWindowState.Normal;
                this.TopMost = true;
                this.TopMost = false;
            } else {
                base.WndProc(ref m);
            }
        }

        /// <summary>
        ///     Starts up the intel reporting component.
        /// </summary>
        protected override void OnShown(EventArgs e) {
            ThreadPool.QueueUserWorkItem((state) => this.intelReporter.Start());
            this.updateCheck.Start();
            this.UpdateStatus();
            base.OnShown(e);
        }

        /// <summary>
        ///     Recenter the panels when returning from minimization.
        /// </summary>
        protected override void OnResize(EventArgs e) {
            if (this.WindowState != FormWindowState.Minimized) {
                this.CenterPanel(this.panelAuthentication);
                this.CenterPanel(this.panelAuthError);
                this.CenterPanel(this.panelChannels);
                this.CenterPanel(this.panelStatus);
            }
            base.OnResize(e);
        }

        /// <summary>
        ///     Hide the window while we are disposing of the session.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e) {
            base.OnClosing(e);

            if (!e.Cancel) {
                this.Visible = false;
            }
        }

        /// <summary>
        ///     Actives the specified pseudo-dialog within the parent window.
        ///     The operation includes centering, z-order manipulation, and
        ///     focus changes.
        /// </summary>
        private void ShowPanel(Control panel, Control focus) {
            // Recenter and show a child panel
            this.CenterPanel(panel);
            panel.BringToFront();
            panel.Visible = true;
            (focus ?? panel).Focus();
        }

        /// <summary>
        ///     Recenter a pseudo-dialog within the window.
        /// </summary>
        private void CenterPanel(Control panel) {
            var margin = this.ClientSize - panel.Size;
            margin.Height /= 2;
            margin.Width /= 2;
            panel.Location = this.ClientRectangle.Location + margin;
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
                panelUpdate.Visible = false;
                labelStatusTitle.Text = title;
                labelStatus.Text = message;
                this.AcceptButton = null;
                this.ShowPanel(this.panelStatus, null);
            }
        }

        /// <summary>
        ///     Displays the channel list pseudo-dialog.
        /// </summary>
        private void ShowChannelList() {
            if (!panelAuthentication.Visible) {
                panelStatus.Visible = false;
                panelAuthError.Visible = false;
                panelChannels.Visible = false;
                if (this.updateEvent != null) {
                    this.panelChannels.Visible = false;
                    this.ShowPanel(this.panelUpdate, this.buttonUpdate);
                    this.AcceptButton = this.buttonUpdate;
                } else if (this.oldStatus != IntelStatus.Active) {
                    // Channel list being shown for the first time
                    this.timerChannels.Enabled = true;
                    this.panelUpdate.Visible = false;
                    this.AcceptButton = null;
                    this.ShowPanel(this.panelChannels, null);
                } else {
                    // Make sure channel list is always centered
                    this.CenterPanel(this.panelChannels);
                }
            }
        }

        /// <summary>
        ///     Performs an UI manipulations required for GUI depicted status.
        /// </summary>
        private void UpdateStatus() {
            // Simple updates
            labelCounts.Text = String.Join(
                Resources.Stats_Join,
                FormatCount(
                    intelReporter.IntelSent,
                    Resources.IntelCount_Zero,
                    Resources.IntelCount_One,
                    Resources.IntelCount_Many),
                FormatCount(
                    intelReporter.IntelDropped,
                    Resources.DropCount_Zero,
                    Resources.DropCount_One,
                    Resources.DropCount_Many),
                FormatCount(
                    intelReporter.Users,
                    Resources.UserCount_Zero,
                    Resources.UserCount_One,
                    Resources.UserCount_Many),
                FormatCount(
                    this.noveltyCount,
                    Resources.NoveltyCount_Zero,
                    Resources.NoveltyCount_One,
                    Resources.NoveltyCount_Many)
            );

            // Authentication errors are handled specially
            if (this.configError && !panelAuthentication.Visible) {
                this.ShowAuthWindow();
            }

            // Update the status string
            var status = intelReporter.Status;
            var statusString = Resources.ResourceManager.GetString(
                    String.Format(
                        CultureInfo.InvariantCulture,
                        "StatusString_{0}", status))
                ?? Resources.StatusString_Unknown;
            labelStatusString.Text = String.Format(
                statusString,
                status,
                intelReporter.Path);

            // Status changes
            switch (status) {
            case IntelStatus.Active:
                // Normal operation
                this.ShowChannelList();
                break;
            case IntelStatus.Waiting:
                // Normal operation
                if (this.updateEvent != null) {
                    this.ShowChannelList();
                } else {
                    this.ShowStatus(
                        Resources.IntelStatus_IdleTitle,
                        Resources.IntelStatus_Idle);
                }
                break;
            case IntelStatus.AuthenticationError:
                // Doesn't like our password
                if (!this.configError) {
                    this.ShowAuthError(
                        Resources.IntelStatus_AuthTitle,
                        Resources.IntelStatus_Auth);
                }
                break;
            case IntelStatus.InvalidPath:
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
            default:
                // This represents a pretty critical failure of the
                // system, so it gets priority over everything, including
                // user entry.
                panelAuthentication.Visible = false;
                this.ShowStatus(
                    Resources.IntelStatus_FatalTitle,
                    Resources.IntelStatus_Fatal);
                break;
            }
            this.oldStatus = status;
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
            // Make sure we aren't disposed/in the process of disposing
            if (this.Disposing || this.InvokeRequired) {
                return;
            }
            
            // Pass up the chain
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
                var logFile = channel.LogFile;
                if (logFile != null) {
                    flag.ImageIndex = 0;
                } else {
                    flag.ImageIndex = 1;
                }
            }
            foreach (var label in channelCounts) {
                var channel = (IntelChannel)label.Tag;
                var count = channel.IntelCount;
                label.Text = FormatCount(
                    channel.IntelCount,
                    String.Empty,
                    Resources.IntelCount_One,
                    Resources.IntelCount_Many);
            }
        }

        /// <summary>
        ///     Formats an integer count using seperate strings for
        ///     zero, one, or many countable items.
        /// </summary>
        private static string FormatCount(int count, string formatZero,
                string formatOne, string formatMany) {
            if (count <= 0) {
                return String.Format(
                    Application.CurrentCulture,
                    formatZero,
                    count);
            } else if (count == 1) {
                return String.Format(
                    Application.CurrentCulture,
                    formatOne,
                    count);
            } else {
                return String.Format(
                    Application.CurrentCulture,
                    formatMany,
                    count);
            }
        }

        /// <summary>
        ///     Hides the channel list if all the channels are active.
        /// </summary>
        private void timerChannels_Tick(object sender, EventArgs e) {
            switch (this.oldStatus) {
            case IntelStatus.Active:
                if(this.updateEvent == null) {
                    // Hide the channel list if everything's green
                    this.panelChannels.Visible = this.intelReporter
                        .Channels
                        .Any();//x => x.Status != IntelStatus.Active);
                } else {
                    // Always going to show the update dialog
                    this.timerChannels.Enabled = false;
                }
                break;
            default:
                // No need to continue watching the channel list
                this.timerChannels.Enabled = false;
                break;
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
            panelUpdate.Visible = false;
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
            panelChannels.Visible = false;
            panelUpdate.Visible = false;
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

            var username = textBoxUsername.Text.Trim();
            var password = textBoxPassword.Text.Trim();
            ThreadPool.QueueUserWorkItem((state) => {
                try {
                    intelReporter.Authenticate(username, password);
                    this.BeginInvoke(new Action(() => {
                        this.panelAuthentication.Visible = false;
                        this.configError = false;
                        this.settings.Username = intelReporter.Username;
                        this.settings.PasswordHash = intelReporter.PasswordHash;
                        this.settings.Save();
                    }));
                } catch (WebException) {
                    this.BeginInvoke(new Action(() => {
                        this.ShowAuthError(
                            Resources.IntelStatus_ErrorTitle,
                            Resources.Authenticate_Network);
                    }));
                } catch (AuthenticationException) {
                    this.BeginInvoke(new Action(() => {
                        this.ShowAuthError(
                            Resources.IntelStatus_AuthTitle,
                            Resources.Authenticate_Auth);
                    }));
                } catch {
                    this.BeginInvoke(new Action(() => {
                        panelAuthentication.Visible = false;
                        this.ShowStatus(
                            Resources.IntelStatus_FatalTitle,
                            Resources.IntelStatus_Fatal);
                    }));
                }
            });
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

        #region Update Checking
        /// <summary>
        ///     Raised by <see cref="updateCheck"/> when a new version has been
        ///     made available.
        /// </summary>
        private void updateCheck_UpdateAvailable(object sender, UpdateEventArgs e) {
            this.updateEvent = e;
            this.buttonUpdate.Enabled = (e.UpdateUri != null)
                && e.UpdateUri.StartsWith("http");
            this.labelUpdateTitle.Text = Resources.IntelStatus_VersionTitle;
            this.labelUpdate.Text = String.Format(
                CultureInfo.CurrentCulture,
                Resources.IntelStatus_Version,
                e.OldVersion,
                e.NewVersion,
                e.UpdateUri);
            this.UpdateStatus();
        }

        /// <summary>
        ///     User has requested we go to the update website.
        /// </summary>
        private void buttonUpdate_Click(object sender, EventArgs e) {
            Process.Start(this.updateEvent.UpdateUri);
        }
        #endregion
    }
}
