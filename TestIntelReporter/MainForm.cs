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

namespace TestIntelReporter {
    public partial class MainForm : Form {
        // Pen used for drawing the panel borders
        private readonly Pen borderPen = new Pen(Color.Gray, 4.0f);
        // Number of times DaBigRedBoat has shown up in a message
        private int noveltyCount;
        // The format string for labelCounts
        private readonly string formatCounts;
        // The most recently reported forum status
        private IntelStatus oldStatus;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MainForm"/> class.
        /// </summary>
        public MainForm() {
            InitializeComponent();

            // Update some fields
            this.Icon = Properties.Resources.AppIcon;
            labelAppName.Text = string.Format(
                Application.CurrentCulture,
                labelAppName.Text,
                Application.ProductVersion);
            // Claim some fields
            formatCounts = labelCounts.Text;
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
        ///     Manages the hiding of the window.
        /// </summary>
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
        }

        /// <summary>
        ///     Actives the specified pseudo-dialog within the parent window.
        ///     The operation includes centering, z-order manipulation, and
        ///     focus changes.
        /// </summary>
        private void ShowPanel(Control panel, Control focus) {
            // Recenter and show a child panel
            if (!panel.Visible) {
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
        }

        /// <summary>
        ///     Displays the status pseudo-dialog.
        /// </summary>
        private void ShowStatus(string title, string message) {
            if (!panelAuthentication.Visible) {
                panelStatus.Visible = false;
                labelStatusTitle.Text = title;
                labelStatus.Text = message;
                this.ShowPanel(this.panelStatus, null);
            }
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

            // Status changes
            var status = intelReporter.Status;
            if (this.oldStatus != status) {
                this.oldStatus = status;
                switch (status) {
                case IntelStatus.Connected:
                    // Normal operation
                    this.panelStatus.Visible = false;
                    break;
                case IntelStatus.Idle:
                    // Normal operation
                    this.ShowStatus(
                        Resources.IntelStatus_IdleTitle,
                        Resources.IntelStatus_Idle);
                    break;
                case IntelStatus.AuthenticationFailure:
                    // Doesn't like our password
                    this.panelStatus.Visible = false;
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
                    // Something broke....seriously
                    this.ShowStatus(
                        Resources.IntelStatus_FatalTitle,
                        Resources.IntelStatus_Fatal);
                    break;
                }
            }
        }

        private void intelReporter_IntelReported(object sender, IntelEventArgs e) {
            // Novelty counter
            if (e.Message.IndexOf("Dabigredboat", StringComparison.OrdinalIgnoreCase) != -1) {
                ++this.noveltyCount;
                this.UpdateStatus();
            }
        }

        private void intelReporter_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            this.UpdateStatus();
        }
    }
}
