namespace BusyLight.Forms;

partial class StatusForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        lblDevices       = new Label();
        pnlDevices       = new FlowLayoutPanel();
        lblPresence      = new Label();
        lblPresenceValue = new Label();
        btnSettings      = new System.Windows.Forms.Button();

        SuspendLayout();

        // lblDevices  (static header)
        lblDevices.AutoSize = true;
        lblDevices.Location = new System.Drawing.Point(12, 12);
        lblDevices.Text     = "BLE Devices:";

        // pnlDevices  (device rows are added dynamically at runtime)
        pnlDevices.Location    = new System.Drawing.Point(12, 32);
        pnlDevices.Size        = new System.Drawing.Size(360, 80);
        pnlDevices.FlowDirection = FlowDirection.TopDown;
        pnlDevices.WrapContents  = false;
        pnlDevices.AutoScroll    = true;

        // lblPresence  (static label)
        lblPresence.AutoSize = true;
        lblPresence.Location = new System.Drawing.Point(12, 122);
        lblPresence.Text     = "Teams:";

        // lblPresenceValue
        lblPresenceValue.AutoSize = true;
        lblPresenceValue.Location = new System.Drawing.Point(100, 122);
        lblPresenceValue.Text     = "—";

        // btnSettings
        btnSettings.Text     = "⚙ Einstellungen";
        btnSettings.Location = new System.Drawing.Point(12, 150);
        btnSettings.Size     = new System.Drawing.Size(130, 26);
        btnSettings.Click   += btnSettings_Click;

        // Form
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new System.Drawing.Size(400, 188);
        Controls.AddRange(new System.Windows.Forms.Control[]
        {
            lblDevices, pnlDevices, lblPresence, lblPresenceValue, btnSettings
        });
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.CenterScreen;
        Text            = "BusyLight";

        ResumeLayout(false);
        PerformLayout();
    }

    // ── Designer-managed controls ─────────────────────────────────────────────

    private Label                        lblDevices       = default!;
    private FlowLayoutPanel              pnlDevices       = default!;
    private Label                        lblPresence      = default!;
    private Label                        lblPresenceValue = default!;
    private System.Windows.Forms.Button  btnSettings      = default!;
}
