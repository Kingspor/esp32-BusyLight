namespace BusyLight.Forms;

partial class BlePickerForm
{
    private System.ComponentModel.IContainer components = null;

    private void InitializeComponent()
    {
        lblStatus   = new Label();
        progressBar = new ProgressBar();
        lstDevices  = new ListBox();
        lblHint     = new Label();
        btnRetry    = new Button();
        btnCancel   = new Button();
        btnSelect   = new Button();
        SuspendLayout();
        //
        // lblStatus
        //
        lblStatus.AutoSize = false;
        lblStatus.Location = new Point(12, 12);
        lblStatus.Name     = "lblStatus";
        lblStatus.Size     = new Size(460, 20);
        lblStatus.TabIndex = 0;
        lblStatus.Text     = "Suche…";
        //
        // progressBar
        //
        progressBar.Location = new Point(12, 36);
        progressBar.Name     = "progressBar";
        progressBar.Size     = new Size(460, 6);
        progressBar.Style    = ProgressBarStyle.Marquee;
        progressBar.TabIndex = 1;
        //
        // lstDevices
        //
        lstDevices.Location = new Point(12, 50);
        lstDevices.Name     = "lstDevices";
        lstDevices.Size     = new Size(460, 130);
        lstDevices.TabIndex = 2;
        lstDevices.SelectedIndexChanged += lstDevices_SelectedIndexChanged;
        //
        // lblHint
        //
        lblHint.AutoSize  = false;
        lblHint.ForeColor = SystemColors.GrayText;
        lblHint.Location  = new Point(12, 188);
        lblHint.Name      = "lblHint";
        lblHint.Size      = new Size(460, 28);
        lblHint.Text      = "Der Scan kann jederzeit über Einstellungen → BLE-Gerät erneut gestartet werden.";
        //
        // btnRetry
        //
        btnRetry.Enabled  = false;
        btnRetry.Location = new Point(12, 224);
        btnRetry.Name     = "btnRetry";
        btnRetry.Size     = new Size(130, 30);
        btnRetry.TabIndex = 3;
        btnRetry.Text     = "Erneut suchen";
        btnRetry.Click   += btnRetry_Click;
        //
        // btnCancel
        //
        btnCancel.Location = new Point(248, 224);
        btnCancel.Name     = "btnCancel";
        btnCancel.Size     = new Size(110, 30);
        btnCancel.TabIndex = 4;
        btnCancel.Text     = "Abbrechen";
        btnCancel.Click   += btnCancel_Click;
        //
        // btnSelect
        //
        btnSelect.Enabled  = false;
        btnSelect.Location = new Point(364, 224);
        btnSelect.Name     = "btnSelect";
        btnSelect.Size     = new Size(108, 30);
        btnSelect.TabIndex = 5;
        btnSelect.Text     = "Auswählen";
        btnSelect.Click   += btnSelect_Click;
        //
        // BlePickerForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(484, 266);
        Controls.Add(lblStatus);
        Controls.Add(progressBar);
        Controls.Add(lstDevices);
        Controls.Add(lblHint);
        Controls.Add(btnRetry);
        Controls.Add(btnCancel);
        Controls.Add(btnSelect);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        Name            = "BlePickerForm";
        StartPosition   = FormStartPosition.CenterScreen;
        Text            = "BusyLight — Gerät auswählen";
        ResumeLayout(false);
        PerformLayout();
    }

    // ── Designer-managed controls ─────────────────────────────────────────────

    private System.Windows.Forms.Label       lblStatus   = default!;
    private System.Windows.Forms.ProgressBar progressBar = default!;
    private System.Windows.Forms.ListBox     lstDevices  = default!;
    private System.Windows.Forms.Label       lblHint     = default!;
    private System.Windows.Forms.Button      btnRetry    = default!;
    private System.Windows.Forms.Button      btnCancel   = default!;
    private System.Windows.Forms.Button      btnSelect   = default!;
}
