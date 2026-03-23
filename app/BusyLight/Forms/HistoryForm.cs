using System.Drawing.Drawing2D;
using System.Drawing.Text;
using BusyLight.Models;

namespace BusyLight.Forms;

/// <summary>
/// History window with two tabs:
///   1. "Akku-Verlauf"    — Line graph of battery voltage over time
///   2. "Status-Verlauf"  — Timeline bar + cumulative summary table
///
/// A status strip at the bottom always shows app uptime and total BLE
/// connected time since program start.
///
/// The form hides instead of closing so it can be re-opened from the
/// tray without losing the history data.
/// </summary>
public sealed class HistoryForm : Form
{
    // ── Double-buffered panel (avoids flicker in custom-drawn controls) ────────

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() { DoubleBuffered = true; }
    }

    // ── Data (passed by reference — always up to date) ────────────────────────

    private readonly List<BatteryDataPoint> _batteryHistory;
    private readonly List<PresenceRecord>   _presenceHistory;
    private readonly Func<TimeSpan>         _getTotalConnected;
    private readonly DateTime               _appStartTime;

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly TabControl             _tabs;
    private readonly BufferedPanel          _batteryPanel;
    private readonly Label                  _lblLastReading;
    private readonly BufferedPanel          _timelinePanel;
    private readonly DataGridView           _summaryGrid;
    private readonly ToolStripStatusLabel   _lblUptime;
    private readonly ToolStripStatusLabel   _lblConnected;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // ── Constructor ───────────────────────────────────────────────────────────

    public HistoryForm(
        List<BatteryDataPoint> batteryHistory,
        List<PresenceRecord>   presenceHistory,
        Func<TimeSpan>         getTotalConnectedTime,
        DateTime               appStartTime)
    {
        _batteryHistory    = batteryHistory;
        _presenceHistory   = presenceHistory;
        _getTotalConnected = getTotalConnectedTime;
        _appStartTime      = appStartTime;

        Text          = "BusyLight — Verlauf";
        Size          = new Size(860, 580);
        MinimumSize   = new Size(600, 420);
        StartPosition = FormStartPosition.CenterScreen;

        // ── Status strip ──────────────────────────────────────────────────────

        var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _lblUptime = new ToolStripStatusLabel("Laufzeit: —")
        {
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            Padding     = new Padding(0, 0, 10, 0),
        };
        _lblConnected = new ToolStripStatusLabel("BLE verbunden: —") { Spring = true };
        statusStrip.Items.AddRange(new ToolStripItem[] { _lblUptime, _lblConnected });
        Controls.Add(statusStrip);

        // ── Tab control ───────────────────────────────────────────────────────

        _tabs = new TabControl { Dock = DockStyle.Fill };
        Controls.Add(_tabs);

        // ── Tab 1: Akku-Verlauf ───────────────────────────────────────────────

        var tabBattery = new TabPage("Akku-Verlauf");
        _tabs.TabPages.Add(tabBattery);

        var batteryToolbar = new Panel { Dock = DockStyle.Top, Height = 32 };
        var btnRefresh     = new Button
        {
            Text      = "Aktualisieren",
            Location  = new Point(4, 4),
            Size      = new Size(110, 24),
            FlatStyle = FlatStyle.System,
        };
        btnRefresh.Click += (_, _) => RefreshAll();

        _lblLastReading = new Label
        {
            Text     = "Kein Messwert",
            AutoSize = true,
            Location = new Point(122, 8),
        };
        batteryToolbar.Controls.AddRange(new Control[] { btnRefresh, _lblLastReading });
        tabBattery.Controls.Add(batteryToolbar);

        _batteryPanel        = new BufferedPanel { Dock = DockStyle.Fill };
        _batteryPanel.Paint  += (_, e) => DrawBatteryGraph(e.Graphics, _batteryPanel.ClientRectangle);
        _batteryPanel.Resize += (_, _) => _batteryPanel.Invalidate();
        tabBattery.Controls.Add(_batteryPanel);

        // ── Tab 2: Status-Verlauf ─────────────────────────────────────────────

        var tabStatus = new TabPage("Status-Verlauf");
        _tabs.TabPages.Add(tabStatus);

        var split = new SplitContainer
        {
            Dock        = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };
        tabStatus.Controls.Add(split);

        _timelinePanel        = new BufferedPanel { Dock = DockStyle.Fill };
        _timelinePanel.Paint  += (_, e) => DrawTimeline(e.Graphics, _timelinePanel.ClientRectangle);
        _timelinePanel.Resize += (_, _) => _timelinePanel.Invalidate();
        split.Panel1.Controls.Add(_timelinePanel);

        _summaryGrid = BuildSummaryGrid();
        split.Panel2.Controls.Add(_summaryGrid);

        // ── Refresh timer ─────────────────────────────────────────────────────

        _refreshTimer         = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick   += (_, _) => RefreshAll();

        // After layout is complete, set the splitter and do first refresh
        Load += (_, _) =>
        {
            split.SplitterDistance = (int)(split.Height * 0.55);
            RefreshAll();
            _refreshTimer.Start();
        };
    }

    // ── Public API (call on UI thread) ────────────────────────────────────────

    /// <summary>Called when a new battery data point was added.</summary>
    public void NotifyBatteryUpdated()
    {
        if (!Visible) return;
        UpdateLastReadingLabel();
        if (_tabs.SelectedTab?.Text == "Akku-Verlauf")
            _batteryPanel.Invalidate();
        UpdateStatusStrip();
    }

    /// <summary>Called when a presence record was added or the last one was closed.</summary>
    public void NotifyPresenceUpdated()
    {
        if (!Visible) return;
        if (_tabs.SelectedTab?.Text == "Status-Verlauf")
        {
            _timelinePanel.Invalidate();
            RefreshSummaryGrid();
        }
        UpdateStatusStrip();
    }

    // ── Internal refresh ──────────────────────────────────────────────────────

    private void RefreshAll()
    {
        UpdateStatusStrip();
        UpdateLastReadingLabel();
        _batteryPanel.Invalidate();
        _timelinePanel.Invalidate();
        RefreshSummaryGrid();
    }

    private void UpdateStatusStrip()
    {
        var uptime    = DateTime.Now - _appStartTime;
        var connected = _getTotalConnected();
        _lblUptime.Text    = $"Laufzeit: {FormatDuration(uptime)}";
        _lblConnected.Text = $"BLE verbunden: {FormatDuration(connected)} (gesamt seit Programmstart)";
    }

    private void UpdateLastReadingLabel()
    {
        if (_batteryHistory.Count == 0)
        {
            _lblLastReading.Text = "Kein Messwert";
            return;
        }
        var last = _batteryHistory[^1];
        _lblLastReading.Text =
            $"Aktuell: {last.Reading}  |  {last.Timestamp:HH:mm:ss}  |  " +
            $"{_batteryHistory.Count} Messwerte";
    }

    // ── Battery Graph ─────────────────────────────────────────────────────────

    private void DrawBatteryGraph(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        const float MinV = 3.0f, MaxV = 4.25f;
        const int PL = 58, PR = 18, PT = 16, PB = 34;

        var plotRect = new RectangleF(
            bounds.X + PL, bounds.Y + PT,
            bounds.Width  - PL - PR,
            bounds.Height - PT - PB);

        // Background
        g.FillRectangle(SystemBrushes.Window, bounds);
        using var plotBg = new SolidBrush(Color.FromArgb(251, 251, 252));
        g.FillRectangle(plotBg, plotRect);

        // Danger zones (subtle fill behind chart)
        float yRed    = VoltToY(3.4f, plotRect, MinV, MaxV);
        float yOrange = VoltToY(3.6f, plotRect, MinV, MaxV);
        using var redZone    = new SolidBrush(Color.FromArgb(18, 220, 60,  60));
        using var orangeZone = new SolidBrush(Color.FromArgb(18, 230, 140,  0));
        g.FillRectangle(redZone,    new RectangleF(plotRect.X, yRed,    plotRect.Width, plotRect.Bottom - yRed));
        g.FillRectangle(orangeZone, new RectangleF(plotRect.X, yOrange, plotRect.Width, yRed - yOrange));

        // Horizontal grid lines + Y-axis labels
        using var gridPen   = new Pen(Color.FromArgb(218, 218, 218));
        using var labelFont = new Font("Segoe UI", 8f);
        var sfRight = new StringFormat { Alignment = StringAlignment.Far };

        for (float v = MinV; v <= MaxV + 0.01f; v += 0.25f)
        {
            float y = VoltToY(v, plotRect, MinV, MaxV);
            g.DrawLine(gridPen, plotRect.X, y, plotRect.Right, y);
            g.DrawString($"{v:F2} V", labelFont, Brushes.DimGray,
                new RectangleF(bounds.X, y - 8, PL - 4, 16), sfRight);
        }

        // Plot border
        using var borderPen = new Pen(Color.Silver);
        g.DrawRectangle(borderPen, plotRect.X, plotRect.Y, plotRect.Width, plotRect.Height);

        // Determine time range
        DateTime tMin, tMax;
        if (_batteryHistory.Count == 0)
        {
            tMin = DateTime.Now.AddMinutes(-30);
            tMax = DateTime.Now;
            DrawTimeAxis(g, plotRect.X, plotRect.Right, plotRect.Bottom + 4, tMin, tMax, labelFont);
            using var ph = new Font("Segoe UI", 10f);
            g.DrawString("Noch keine Messwerte", ph, Brushes.LightGray, plotRect,
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            return;
        }

        tMin = _batteryHistory[0].Timestamp;
        tMax = _batteryHistory[^1].Timestamp;
        if ((tMax - tMin).TotalSeconds < 10)
            tMax = tMin + TimeSpan.FromMinutes(5);

        // Shaded area under curve
        var areaPoints = new List<PointF>
        {
            new(TimeToX(_batteryHistory[0].Timestamp, plotRect, tMin, tMax), plotRect.Bottom),
        };
        foreach (var pt in _batteryHistory)
            areaPoints.Add(new PointF(
                TimeToX(pt.Timestamp,        plotRect, tMin, tMax),
                VoltToY(pt.Reading.VoltageV, plotRect, MinV, MaxV)));
        areaPoints.Add(new PointF(TimeToX(_batteryHistory[^1].Timestamp, plotRect, tMin, tMax), plotRect.Bottom));
        using var areaBrush = new SolidBrush(Color.FromArgb(35, 70, 130, 220));
        g.FillPolygon(areaBrush, areaPoints.ToArray());

        // Line: segment-by-segment, colored by SoC
        using var penGreen  = new Pen(Color.FromArgb(30,  160,  80), 2f);
        using var penOrange = new Pen(Color.FromArgb(220, 140,   0), 2f);
        using var penRed    = new Pen(Color.FromArgb(210,  55,  55), 2f);

        for (int i = 1; i < _batteryHistory.Count; i++)
        {
            var prev = _batteryHistory[i - 1];
            var curr = _batteryHistory[i];

            float x1 = TimeToX(prev.Timestamp, plotRect, tMin, tMax);
            float y1 = VoltToY(prev.Reading.VoltageV, plotRect, MinV, MaxV);
            float x2 = TimeToX(curr.Timestamp, plotRect, tMin, tMax);
            float y2 = VoltToY(curr.Reading.VoltageV, plotRect, MinV, MaxV);

            var pen = curr.Reading.SocPercent < 20 ? penRed
                    : curr.Reading.SocPercent < 40 ? penOrange
                    : penGreen;
            g.DrawLine(pen, x1, y1, x2, y2);
        }

        // Dots at each measurement
        foreach (var pt in _batteryHistory)
        {
            float x    = TimeToX(pt.Timestamp, plotRect, tMin, tMax);
            float y    = VoltToY(pt.Reading.VoltageV, plotRect, MinV, MaxV);
            var dotClr = pt.Reading.SocPercent < 20 ? Color.FromArgb(210, 55, 55)
                       : pt.Reading.SocPercent < 40 ? Color.FromArgb(220, 140, 0)
                       : Color.FromArgb(30, 160, 80);
            using var dot = new SolidBrush(dotClr);
            g.FillEllipse(dot, x - 3f, y - 3f, 6f, 6f);
        }

        // Time axis
        DrawTimeAxis(g, plotRect.X, plotRect.Right, plotRect.Bottom + 4, tMin, tMax, labelFont);
    }

    // ── Status Timeline ────────────────────────────────────────────────────────

    private void DrawTimeline(Graphics g, Rectangle bounds)
    {
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        const int PL = 12, PR = 90, PT = 14, PB = 34;

        var plotRect = new RectangleF(
            bounds.X + PL, bounds.Y + PT,
            bounds.Width  - PL - PR,
            bounds.Height - PT - PB);

        g.FillRectangle(SystemBrushes.Window, bounds);

        DateTime tMin     = _appStartTime;
        DateTime tMax     = DateTime.Now;
        double   totalSec = Math.Max(1, (tMax - tMin).TotalSeconds);

        float barH = Math.Min(40f, plotRect.Height - 14f);
        float barY = plotRect.Y + (plotRect.Height - PB - barH) / 2f;

        // Empty bar background
        g.FillRectangle(SystemBrushes.ControlLight,
            new RectangleF(plotRect.X, barY, plotRect.Width, barH));

        using var labelFont = new Font("Segoe UI", 8f);
        var sfCenter = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags   = StringFormatFlags.NoWrap,
        };

        foreach (var rec in _presenceHistory)
        {
            double s1 = Math.Max(0, (rec.Start - tMin).TotalSeconds);
            double s2 = Math.Min(totalSec, ((rec.End ?? DateTime.Now) - tMin).TotalSeconds);
            if (s2 <= s1) continue;

            float x1 = plotRect.X + (float)(s1 / totalSec * plotRect.Width);
            float x2 = plotRect.X + (float)(s2 / totalSec * plotRect.Width);
            float w  = Math.Max(1f, x2 - x1);

            var baseColor = StatusColor(rec.Availability);
            var barRect   = new RectangleF(x1, barY, w, barH);

            if (rec.IsOverride)
            {
                using var lightBrush = new SolidBrush(Color.FromArgb(190, baseColor));
                g.FillRectangle(lightBrush, barRect);
                using var hatch = new HatchBrush(
                    HatchStyle.LightUpwardDiagonal,
                    Color.FromArgb(70, 255, 255, 255),
                    Color.Transparent);
                g.FillRectangle(hatch, barRect);
            }
            else
            {
                using var brush = new SolidBrush(baseColor);
                g.FillRectangle(brush, barRect);
            }

            // Label inside bar when wide enough
            if (w > 28)
            {
                var abbrev = AbbrevStatus(rec.Availability) + (rec.IsOverride ? "*" : "");
                g.DrawString(abbrev, labelFont, Brushes.White, barRect, sfCenter);
            }
        }

        // Bar border
        using var barBorder = new Pen(Color.FromArgb(160, 160, 160));
        g.DrawRectangle(barBorder, plotRect.X, barY, plotRect.Width, barH);

        // No data
        if (_presenceHistory.Count == 0)
        {
            using var phFont = new Font("Segoe UI", 10f);
            g.DrawString("Noch keine Verlaufsdaten", phFont, Brushes.LightGray,
                new RectangleF(plotRect.X, barY, plotRect.Width, barH), sfCenter);
        }

        // Time axis
        DrawTimeAxis(g, plotRect.X, plotRect.Right, barY + barH + 4, tMin, tMax, labelFont);

        // Legend (top right)
        g.DrawString("* = Override", labelFont, Brushes.Gray,
            new PointF(bounds.Right - PR + 4f, bounds.Top + 4f));
    }

    // ── Shared drawing helpers ────────────────────────────────────────────────

    private static float VoltToY(float v, RectangleF r, float minV, float maxV)
        => r.Bottom - (v - minV) / (maxV - minV) * r.Height;

    private static float TimeToX(DateTime t, RectangleF r, DateTime tMin, DateTime tMax)
    {
        double frac = (t - tMin).TotalSeconds / (tMax - tMin).TotalSeconds;
        return r.X + (float)(frac * r.Width);
    }

    private static void DrawTimeAxis(
        Graphics g,
        float xLeft, float xRight, float yBase,
        DateTime tMin, DateTime tMax,
        Font font)
    {
        double spanMin = (tMax - tMin).TotalMinutes;
        int tickMin    = spanMin <=   5 ?   1
                       : spanMin <=  30 ?   5
                       : spanMin <= 120 ?  15
                       : spanMin <= 360 ?  30
                       : spanMin <= 720 ?  60 : 120;

        // Round tMin up to the next tick boundary
        int boundary = ((int)tMin.TimeOfDay.TotalMinutes / tickMin + 1) * tickMin;
        var tick = new DateTime(tMin.Year, tMin.Month, tMin.Day)
            .AddMinutes(boundary);

        using var tickPen = new Pen(Color.FromArgb(175, 175, 175));

        while (tick <= tMax)
        {
            double frac = (tick - tMin).TotalSeconds / (tMax - tMin).TotalSeconds;
            float  x    = xLeft + (float)(frac * (xRight - xLeft));

            g.DrawLine(tickPen, x, yBase, x, yBase + 4f);

            string lbl = tick.ToString("HH:mm");
            var    sz  = g.MeasureString(lbl, font);
            g.DrawString(lbl, font, Brushes.DimGray, x - sz.Width / 2f, yBase + 5f);

            tick = tick.AddMinutes(tickMin);
        }
    }

    // ── Summary Grid ──────────────────────────────────────────────────────────

    private DataGridView BuildSummaryGrid()
    {
        var dgv = new DataGridView
        {
            Dock                  = DockStyle.Fill,
            ReadOnly              = true,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible     = false,
            BackgroundColor       = SystemColors.Window,
            BorderStyle           = BorderStyle.None,
            SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode =
                DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        };
        dgv.RowTemplate.Height = 26;

        dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Status",   HeaderText = "Status",    FillWeight = 35 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Teams",    HeaderText = "Teams",     FillWeight = 22 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Override", HeaderText = "Override",  FillWeight = 22 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Gesamt",   HeaderText = "Gesamt",    FillWeight = 21 });

        dgv.CellPainting += OnSummaryCellPainting;
        return dgv;
    }

    private void RefreshSummaryGrid()
    {
        var totals = new Dictionary<string, (TimeSpan Teams, TimeSpan Override)>();

        foreach (var rec in _presenceHistory)
        {
            if (!totals.TryGetValue(rec.Availability, out var t))
                t = (TimeSpan.Zero, TimeSpan.Zero);

            if (rec.IsOverride) t = (t.Teams,              t.Override + rec.Duration);
            else                t = (t.Teams + rec.Duration, t.Override);

            totals[rec.Availability] = t;
        }

        _summaryGrid.Rows.Clear();
        foreach (var (key, (teams, over)) in totals
            .OrderByDescending(kv => (kv.Value.Teams + kv.Value.Override).TotalSeconds))
        {
            _summaryGrid.Rows.Add(
                key,
                FormatDuration(teams),
                over == TimeSpan.Zero ? "—" : FormatDuration(over),
                FormatDuration(teams + over));
        }
    }

    private void OnSummaryCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= _summaryGrid.Rows.Count)
            return;

        e.PaintBackground(e.ClipBounds, true);

        var key   = _summaryGrid.Rows[e.RowIndex].Cells[0].Value as string ?? "";
        var color = StatusColor(key);

        // Colored dot
        const int DotSize = 10;
        int dotX = e.CellBounds.X + 5;
        int dotY = e.CellBounds.Y + (e.CellBounds.Height - DotSize) / 2;

        if (e.Graphics is null) { e.Handled = true; return; }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var dotBrush = new SolidBrush(color);
        e.Graphics.FillEllipse(dotBrush, dotX, dotY, DotSize, DotSize);
        e.Graphics.SmoothingMode = SmoothingMode.Default;

        // Status text
        bool selected = _summaryGrid.Rows[e.RowIndex].Selected;
        var  textBrush = selected ? SystemBrushes.HighlightText : SystemBrushes.ControlText;
        e.Graphics.DrawString(
            key,
            _summaryGrid.Font,
            textBrush,
            new RectangleF(dotX + DotSize + 4, e.CellBounds.Y,
                           e.CellBounds.Width - DotSize - 12, e.CellBounds.Height),
            new StringFormat { LineAlignment = StringAlignment.Center });

        e.Handled = true;
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static Color StatusColor(string key) => key switch
    {
        "Available"       => Color.FromArgb(0x10, 0xA3, 0x19),
        "Busy"            => Color.FromArgb(0xC4, 0x27, 0x2F),
        "DoNotDisturb"    => Color.FromArgb(0xC4, 0x27, 0x2F),
        "Away"            => Color.FromArgb(0xFF, 0xAA, 0x44),
        "BeRightBack"     => Color.FromArgb(0xFF, 0xAA, 0x44),
        "Offline"         => Color.FromArgb(0x74, 0x74, 0x74),
        "PresenceUnknown" => Color.FromArgb(0x74, 0x74, 0x74),
        _                 => Color.Gray,
    };

    private static string AbbrevStatus(string key) => key switch
    {
        "Available"       => "Verfügbar",
        "Busy"            => "Beschäftigt",
        "DoNotDisturb"    => "DnD",
        "Away"            => "Abwesend",
        "BeRightBack"     => "BRB",
        "Offline"         => "Offline",
        "PresenceUnknown" => "?",
        _                 => key,
    };

    internal static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds <  60) return $"{(int)ts.TotalSeconds} s";
        if (ts.TotalMinutes <  60) return $"{(int)ts.TotalMinutes} min";
        return $"{(int)ts.TotalHours} h {ts.Minutes:D2} min";
    }

    // ── Form lifetime ──────────────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            RefreshAll();
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refreshTimer.Dispose();
        base.Dispose(disposing);
    }
}
