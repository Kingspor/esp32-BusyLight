namespace BusyLight.Forms;

partial class ColorWheelForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _wheelBitmap?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        pnlWheel      = new System.Windows.Forms.Panel();
        pnlBrightness = new System.Windows.Forms.Panel();
        pnlPreview    = new System.Windows.Forms.Panel();
        lblHex        = new System.Windows.Forms.Label();
        lblRgb        = new System.Windows.Forms.Label();
        btnOk         = new System.Windows.Forms.Button();
        btnCancel     = new System.Windows.Forms.Button();

        SuspendLayout();

        // pnlWheel — HSV colour wheel (220 × 220)
        pnlWheel.Location  = new System.Drawing.Point(12, 12);
        pnlWheel.Size      = new System.Drawing.Size(220, 220);
        pnlWheel.Cursor    = System.Windows.Forms.Cursors.Cross;

        // pnlBrightness — vertical value/brightness strip (28 × 220)
        pnlBrightness.Location  = new System.Drawing.Point(242, 12);
        pnlBrightness.Size      = new System.Drawing.Size(28, 220);
        pnlBrightness.Cursor    = System.Windows.Forms.Cursors.HSplit;

        // pnlPreview — current colour swatch
        pnlPreview.Location    = new System.Drawing.Point(12, 244);
        pnlPreview.Size        = new System.Drawing.Size(60, 40);
        pnlPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

        // lblHex
        lblHex.AutoSize  = true;
        lblHex.Location  = new System.Drawing.Point(82, 246);
        lblHex.Text      = "#000000";
        lblHex.Font      = new System.Drawing.Font("Consolas", 12f, System.Drawing.FontStyle.Bold);

        // lblRgb
        lblRgb.AutoSize = true;
        lblRgb.Location = new System.Drawing.Point(82, 268);
        lblRgb.Text     = "R: 0   G: 0   B: 0";

        // btnOk
        btnOk.Text     = "OK";
        btnOk.Location = new System.Drawing.Point(117, 298);
        btnOk.Size     = new System.Drawing.Size(72, 28);
        btnOk.Click   += btnOk_Click;

        // btnCancel
        btnCancel.Text     = "Abbrechen";
        btnCancel.Location = new System.Drawing.Point(198, 298);
        btnCancel.Size     = new System.Drawing.Size(80, 28);
        btnCancel.Click   += btnCancel_Click;

        // Form
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize          = new System.Drawing.Size(290, 338);
        Controls.AddRange(new System.Windows.Forms.Control[]
        {
            pnlWheel, pnlBrightness, pnlPreview,
            lblHex, lblRgb, btnOk, btnCancel
        });
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = System.Windows.Forms.FormStartPosition.CenterParent;
        Text            = "Farbe wählen";

        ResumeLayout(false);
        PerformLayout();
    }

    private System.Windows.Forms.Panel  pnlWheel      = default!;
    private System.Windows.Forms.Panel  pnlBrightness = default!;
    private System.Windows.Forms.Panel  pnlPreview    = default!;
    private System.Windows.Forms.Label  lblHex        = default!;
    private System.Windows.Forms.Label  lblRgb        = default!;
    private System.Windows.Forms.Button btnOk         = default!;
    private System.Windows.Forms.Button btnCancel     = default!;
}
