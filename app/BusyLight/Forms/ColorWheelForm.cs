namespace BusyLight.Forms;

/// <summary>
/// Modal colour-picker dialog featuring an HSV colour wheel and a brightness strip.
/// Usage:
///   using var dlg = new ColorWheelForm(initialColor);
///   if (dlg.ShowDialog(owner) == DialogResult.OK) { use dlg.SelectedColor; }
/// </summary>
public sealed partial class ColorWheelForm : Form
{
    // ── Result ────────────────────────────────────────────────────────────────

    /// <summary>The colour selected by the user (valid after DialogResult == OK).</summary>
    public Color SelectedColor { get; private set; }

    /// <summary>
    /// Raised on every interactive change (wheel drag, slider drag) so the caller
    /// can show a live preview before the user clicks OK.
    /// </summary>
    public event EventHandler<Color>? ColorChanged;

    // ── HSV state ─────────────────────────────────────────────────────────────

    private float _hue;        // 0 – 360
    private float _saturation; // 0 – 1
    private float _value;      // 0 – 1  (brightness)

    // ── Cached wheel bitmap (rebuilt when _value changes) ─────────────────────

    private Bitmap? _wheelBitmap;
    private bool    _draggingWheel;
    private bool    _draggingSlider;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ColorWheelForm(Color initial)
    {
        InitializeComponent();

        RgbToHsv(initial.R, initial.G, initial.B,
                 out _hue, out _saturation, out _value);
        SelectedColor = initial;

        // Wire up paint + mouse events (done here, not in Designer,
        // because the logic is tightly coupled with private fields).
        pnlWheel.Paint      += PnlWheel_Paint;
        pnlWheel.MouseDown  += PnlWheel_MouseDown;
        pnlWheel.MouseMove  += PnlWheel_MouseMove;
        pnlWheel.MouseUp    += (_, _) => _draggingWheel = false;
        pnlWheel.Resize     += (_, _) => { _wheelBitmap = null; pnlWheel.Invalidate(); };

        pnlBrightness.Paint     += PnlBrightness_Paint;
        pnlBrightness.MouseDown += PnlBrightness_MouseDown;
        pnlBrightness.MouseMove += PnlBrightness_MouseMove;
        pnlBrightness.MouseUp   += (_, _) => _draggingSlider = false;

        UpdatePreview();
    }

    // ── Colour wheel ──────────────────────────────────────────────────────────

    private void RebuildWheelBitmap()
    {
        int size = pnlWheel.Width;
        if (size <= 0) return;

        _wheelBitmap?.Dispose();
        _wheelBitmap = new Bitmap(size, size);

        float cx = size / 2f, cy = size / 2f, r = size / 2f - 2f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx   = x - cx, dy = y - cy;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist <= r)
            {
                float hue = (MathF.Atan2(dy, dx) * (180f / MathF.PI) + 360f) % 360f;
                float sat = dist / r;
                _wheelBitmap.SetPixel(x, y, HsvToRgb(hue, sat, _value));
            }
        }
    }

    private void PnlWheel_Paint(object? sender, PaintEventArgs e)
    {
        if (_wheelBitmap is null || _wheelBitmap.Width != pnlWheel.Width)
            RebuildWheelBitmap();

        if (_wheelBitmap is not null)
            e.Graphics.DrawImage(_wheelBitmap, 0, 0);

        // Crosshair marker at current H/S position
        float cx  = pnlWheel.Width  / 2f;
        float cy  = pnlWheel.Height / 2f;
        float r   = pnlWheel.Width  / 2f - 2f;
        float rad = _hue * MathF.PI / 180f;
        float px  = cx + r * _saturation * MathF.Cos(rad);
        float py  = cy + r * _saturation * MathF.Sin(rad);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var white = new Pen(Color.White, 2f);
        using var black = new Pen(Color.Black, 1f);
        e.Graphics.DrawEllipse(white, px - 6, py - 6, 12, 12);
        e.Graphics.DrawEllipse(black, px - 6, py - 6, 12, 12);
    }

    private void PnlWheel_MouseDown(object? sender, MouseEventArgs e)
    {
        _draggingWheel = true;
        PickFromWheel(e.X, e.Y);
    }

    private void PnlWheel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingWheel) PickFromWheel(e.X, e.Y);
    }

    private void PickFromWheel(int mx, int my)
    {
        float cx = pnlWheel.Width  / 2f;
        float cy = pnlWheel.Height / 2f;
        float r  = pnlWheel.Width  / 2f - 2f;
        float dx = mx - cx, dy = my - cy;

        _hue        = (MathF.Atan2(dy, dx) * (180f / MathF.PI) + 360f) % 360f;
        _saturation = Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) / r, 0f, 1f);

        pnlWheel.Invalidate();
        pnlBrightness.Invalidate();
        UpdatePreview();
    }

    // ── Brightness strip ──────────────────────────────────────────────────────

    private void PnlBrightness_Paint(object? sender, PaintEventArgs e)
    {
        int w = pnlBrightness.Width, h = pnlBrightness.Height;

        // Gradient: black (bottom) → full-saturation colour at current H/S (top)
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Point(0, h), new Point(0, 0),
            Color.Black,
            HsvToRgb(_hue, _saturation, 1f));
        e.Graphics.FillRectangle(brush, 0, 0, w, h);

        // Marker line
        int markerY = (int)((1f - _value) * (h - 2)) + 1;
        using var pw = new Pen(Color.White, 2f);
        using var pb = new Pen(Color.Black, 1f);
        e.Graphics.DrawLine(pw, 0, markerY, w, markerY);
        e.Graphics.DrawLine(pb, 0, markerY + 1, w, markerY + 1);
    }

    private void PnlBrightness_MouseDown(object? sender, MouseEventArgs e)
    {
        _draggingSlider = true;
        PickBrightness(e.Y);
    }

    private void PnlBrightness_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingSlider) PickBrightness(e.Y);
    }

    private void PickBrightness(int my)
    {
        _value = 1f - Math.Clamp((float)my / pnlBrightness.Height, 0f, 1f);

        // Value affects wheel colours → invalidate both
        _wheelBitmap = null;
        pnlWheel.Invalidate();
        pnlBrightness.Invalidate();
        UpdatePreview();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        SelectedColor        = HsvToRgb(_hue, _saturation, _value);
        pnlPreview.BackColor = SelectedColor;
        lblHex.Text          = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        lblRgb.Text          = $"R: {SelectedColor.R,3}   G: {SelectedColor.G,3}   B: {SelectedColor.B,3}";

        // Notify the caller of every interactive change for live LED preview
        ColorChanged?.Invoke(this, SelectedColor);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void btnOk_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ── HSV ↔ RGB ─────────────────────────────────────────────────────────────

    internal static Color HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        (float r, float g, float b) = ((int)(h / 60f)) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return Color.FromArgb(
            Math.Clamp((int)((r + m) * 255f), 0, 255),
            Math.Clamp((int)((g + m) * 255f), 0, 255),
            Math.Clamp((int)((b + m) * 255f), 0, 255));
    }

    internal static void RgbToHsv(byte r, byte g, byte b,
                                   out float h, out float s, out float v)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max   = Math.Max(rf, Math.Max(gf, bf));
        float min   = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        v = max;
        s = max == 0f ? 0f : delta / max;

        if (delta == 0f) { h = 0f; return; }

        if      (max == rf) h = 60f * (((gf - bf) / delta) % 6f);
        else if (max == gf) h = 60f * (((bf - rf) / delta) + 2f);
        else                h = 60f * (((rf - gf) / delta) + 4f);

        if (h < 0f) h += 360f;
    }
}
