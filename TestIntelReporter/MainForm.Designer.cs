namespace TestIntelReporter
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.Label labelUsername;
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            System.Windows.Forms.Label labelPassword;
            System.Windows.Forms.Label labelAuthenticationTitle;
            this.labelStatus = new System.Windows.Forms.Label();
            this.panelAuthentication = new System.Windows.Forms.TableLayoutPanel();
            this.textBoxUsername = new System.Windows.Forms.TextBox();
            this.textBoxPassword = new System.Windows.Forms.TextBox();
            this.buttonLogin = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.labelAuthentication = new System.Windows.Forms.Label();
            this.panelAuthError = new System.Windows.Forms.TableLayoutPanel();
            this.buttonChangeAuth = new System.Windows.Forms.Button();
            this.labelAuthError = new System.Windows.Forms.Label();
            this.tableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.pictureBoxDino = new System.Windows.Forms.PictureBox();
            this.labelAppName = new System.Windows.Forms.Label();
            this.labelCounts = new System.Windows.Forms.Label();
            this.panelStatus = new System.Windows.Forms.TableLayoutPanel();
            this.labelStatusTitle = new System.Windows.Forms.Label();
            this.intelReporter = new PleaseIgnore.IntelMap.IntelReporter(this.components);
            labelUsername = new System.Windows.Forms.Label();
            labelPassword = new System.Windows.Forms.Label();
            labelAuthenticationTitle = new System.Windows.Forms.Label();
            this.panelAuthentication.SuspendLayout();
            this.panelAuthError.SuspendLayout();
            this.tableLayoutPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDino)).BeginInit();
            this.panelStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.intelReporter)).BeginInit();
            this.SuspendLayout();
            // 
            // labelUsername
            // 
            resources.ApplyResources(labelUsername, "labelUsername");
            this.panelAuthentication.SetColumnSpan(labelUsername, 2);
            labelUsername.Name = "labelUsername";
            // 
            // labelPassword
            // 
            resources.ApplyResources(labelPassword, "labelPassword");
            this.panelAuthentication.SetColumnSpan(labelPassword, 2);
            labelPassword.Name = "labelPassword";
            // 
            // labelAuthenticationTitle
            // 
            resources.ApplyResources(labelAuthenticationTitle, "labelAuthenticationTitle");
            this.panelAuthentication.SetColumnSpan(labelAuthenticationTitle, 2);
            labelAuthenticationTitle.Name = "labelAuthenticationTitle";
            // 
            // labelStatus
            // 
            resources.ApplyResources(this.labelStatus, "labelStatus");
            this.labelStatus.Name = "labelStatus";
            // 
            // panelAuthentication
            // 
            resources.ApplyResources(this.panelAuthentication, "panelAuthentication");
            this.panelAuthentication.BackColor = System.Drawing.Color.DimGray;
            this.panelAuthentication.Controls.Add(labelUsername, 0, 1);
            this.panelAuthentication.Controls.Add(this.textBoxUsername, 0, 2);
            this.panelAuthentication.Controls.Add(labelPassword, 0, 3);
            this.panelAuthentication.Controls.Add(this.textBoxPassword, 0, 4);
            this.panelAuthentication.Controls.Add(this.buttonLogin, 0, 5);
            this.panelAuthentication.Controls.Add(this.buttonCancel, 1, 5);
            this.panelAuthentication.Controls.Add(this.labelAuthentication, 0, 6);
            this.panelAuthentication.Controls.Add(labelAuthenticationTitle, 0, 0);
            this.panelAuthentication.Name = "panelAuthentication";
            this.panelAuthentication.Paint += new System.Windows.Forms.PaintEventHandler(this.DrawBorder);
            // 
            // textBoxUsername
            // 
            resources.ApplyResources(this.textBoxUsername, "textBoxUsername");
            this.textBoxUsername.BackColor = System.Drawing.Color.White;
            this.panelAuthentication.SetColumnSpan(this.textBoxUsername, 2);
            this.textBoxUsername.ForeColor = System.Drawing.Color.Black;
            this.textBoxUsername.Name = "textBoxUsername";
            // 
            // textBoxPassword
            // 
            resources.ApplyResources(this.textBoxPassword, "textBoxPassword");
            this.textBoxPassword.BackColor = System.Drawing.Color.White;
            this.panelAuthentication.SetColumnSpan(this.textBoxPassword, 2);
            this.textBoxPassword.ForeColor = System.Drawing.Color.Black;
            this.textBoxPassword.Name = "textBoxPassword";
            // 
            // buttonLogin
            // 
            resources.ApplyResources(this.buttonLogin, "buttonLogin");
            this.buttonLogin.Name = "buttonLogin";
            this.buttonLogin.UseVisualStyleBackColor = true;
            // 
            // buttonCancel
            // 
            resources.ApplyResources(this.buttonCancel, "buttonCancel");
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.UseVisualStyleBackColor = true;
            // 
            // labelAuthentication
            // 
            resources.ApplyResources(this.labelAuthentication, "labelAuthentication");
            this.panelAuthentication.SetColumnSpan(this.labelAuthentication, 2);
            this.labelAuthentication.Name = "labelAuthentication";
            // 
            // panelAuthError
            // 
            resources.ApplyResources(this.panelAuthError, "panelAuthError");
            this.panelAuthError.BackColor = System.Drawing.Color.DimGray;
            this.panelAuthError.Controls.Add(this.buttonChangeAuth, 0, 1);
            this.panelAuthError.Controls.Add(this.labelAuthError, 0, 0);
            this.panelAuthError.Name = "panelAuthError";
            this.panelAuthError.Paint += new System.Windows.Forms.PaintEventHandler(this.DrawBorder);
            // 
            // buttonChangeAuth
            // 
            resources.ApplyResources(this.buttonChangeAuth, "buttonChangeAuth");
            this.buttonChangeAuth.Name = "buttonChangeAuth";
            this.buttonChangeAuth.UseVisualStyleBackColor = true;
            // 
            // labelAuthError
            // 
            resources.ApplyResources(this.labelAuthError, "labelAuthError");
            this.labelAuthError.Name = "labelAuthError";
            // 
            // tableLayoutPanel
            // 
            resources.ApplyResources(this.tableLayoutPanel, "tableLayoutPanel");
            this.tableLayoutPanel.Controls.Add(this.pictureBoxDino, 0, 1);
            this.tableLayoutPanel.Controls.Add(this.labelAppName, 1, 1);
            this.tableLayoutPanel.Controls.Add(this.labelCounts, 1, 2);
            this.tableLayoutPanel.Name = "tableLayoutPanel";
            // 
            // pictureBoxDino
            // 
            this.pictureBoxDino.Image = global::TestIntelReporter.Properties.Resources.AboutImage;
            resources.ApplyResources(this.pictureBoxDino, "pictureBoxDino");
            this.pictureBoxDino.Name = "pictureBoxDino";
            this.tableLayoutPanel.SetRowSpan(this.pictureBoxDino, 3);
            this.pictureBoxDino.TabStop = false;
            // 
            // labelAppName
            // 
            resources.ApplyResources(this.labelAppName, "labelAppName");
            this.labelAppName.Name = "labelAppName";
            // 
            // labelCounts
            // 
            resources.ApplyResources(this.labelCounts, "labelCounts");
            this.labelCounts.Name = "labelCounts";
            // 
            // panelStatus
            // 
            resources.ApplyResources(this.panelStatus, "panelStatus");
            this.panelStatus.BackColor = System.Drawing.Color.DimGray;
            this.panelStatus.Controls.Add(this.labelStatus, 0, 1);
            this.panelStatus.Controls.Add(this.labelStatusTitle, 0, 0);
            this.panelStatus.Name = "panelStatus";
            this.panelStatus.Paint += new System.Windows.Forms.PaintEventHandler(this.DrawBorder);
            // 
            // labelStatusTitle
            // 
            resources.ApplyResources(this.labelStatusTitle, "labelStatusTitle");
            this.labelStatusTitle.Name = "labelStatusTitle";
            // 
            // intelReporter
            // 
            this.intelReporter.SynchronizingObject = this;
            this.intelReporter.IntelReported += new System.EventHandler<PleaseIgnore.IntelMap.IntelEventArgs>(this.intelReporter_IntelReported);
            this.intelReporter.PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler(this.intelReporter_PropertyChanged);
            // 
            // MainForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(40)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.panelStatus);
            this.Controls.Add(this.panelAuthError);
            this.Controls.Add(this.panelAuthentication);
            this.Controls.Add(this.tableLayoutPanel);
            this.ForeColor = System.Drawing.Color.White;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.panelAuthentication.ResumeLayout(false);
            this.panelAuthentication.PerformLayout();
            this.panelAuthError.ResumeLayout(false);
            this.panelAuthError.PerformLayout();
            this.tableLayoutPanel.ResumeLayout(false);
            this.tableLayoutPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDino)).EndInit();
            this.panelStatus.ResumeLayout(false);
            this.panelStatus.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.intelReporter)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel panelAuthentication;
        private System.Windows.Forms.TextBox textBoxUsername;
        private System.Windows.Forms.TextBox textBoxPassword;
        private System.Windows.Forms.Button buttonLogin;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.Label labelAuthentication;
        private System.Windows.Forms.TableLayoutPanel panelAuthError;
        private System.Windows.Forms.Button buttonChangeAuth;
        private System.Windows.Forms.Label labelAuthError;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel;
        private System.Windows.Forms.PictureBox pictureBoxDino;
        private System.Windows.Forms.Label labelAppName;
        private System.Windows.Forms.Label labelCounts;
        private System.Windows.Forms.TableLayoutPanel panelStatus;
        private PleaseIgnore.IntelMap.IntelReporter intelReporter;
        private System.Windows.Forms.Label labelStatusTitle;
        private System.Windows.Forms.Label labelStatus;

    }
}

