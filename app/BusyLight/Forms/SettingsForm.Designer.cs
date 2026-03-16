namespace BusyLight.Forms;

partial class SettingsForm
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
        menuStrip       = new MenuStrip();
        mnuDatei        = new ToolStripMenuItem();
        mnuLaden        = new ToolStripMenuItem();
        mnuSpeichern    = new ToolStripMenuItem();
        mnuTrennstrich  = new ToolStripSeparator();
        mnuSchliessen   = new ToolStripMenuItem();
        mnuHilfe        = new ToolStripMenuItem();
        mnuDokumentation = new ToolStripMenuItem();
        mnuLogOeffnen   = new ToolStripMenuItem();
        tabControl      = new TabControl();
        tabAllgemein    = new TabPage();
        grpAzure        = new GroupBox();
        lblClientId     = new Label();
        txtClientId     = new TextBox();
        lblTenantId     = new Label();
        txtTenantId     = new TextBox();
        grpPolling      = new GroupBox();
        lblGraphInterval = new Label();
        nudGraphInterval = new NumericUpDown();
        lblBleRetry      = new Label();
        nudBleRetry      = new NumericUpDown();
        lblBrightnessCap = new Label();
        nudBrightnessCap = new NumericUpDown();
        tabPraesenz      = new TabPage();
        lblPresenceHint  = new Label();
        chkLivePreview   = new CheckBox();
        lblCurrPresenceCaption = new Label();
        lblCurrPresence        = new Label();
        btnFetchNow            = new Button();
        lblOverrideCaption     = new Label();
        cmbOverride            = new ComboBox();
        btnApplyOverride       = new Button();
        lblActiveOverride      = new Label();
        chkKeepOverride        = new CheckBox();
        pnlPresence     = new Panel();
        tabBle          = new TabPage();
        lblBleInfo      = new Label();
        lblBleDevice    = new Label();
        btnRescan       = new Button();
        btnStopScan     = new Button();
        tabZuordnung    = new TabPage();
        lblZuordnungHint = new Label();
        pnlZuordnung    = new Panel();
        statusStrip  = new StatusStrip();
        sslTeams     = new ToolStripStatusLabel();
        sslSep       = new ToolStripStatusLabel();
        sslBle       = new ToolStripStatusLabel();
        sslSep2      = new ToolStripStatusLabel();
        sslProtocol  = new ToolStripStatusLabel();
        sslSep3      = new ToolStripStatusLabel();
        sslVersion   = new ToolStripStatusLabel();

        tabZuordnung.SuspendLayout();
        menuStrip.SuspendLayout();
        tabControl.SuspendLayout();
        tabAllgemein.SuspendLayout();
        grpAzure.SuspendLayout();
        grpPolling.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)nudGraphInterval).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudBleRetry).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudBrightnessCap).BeginInit();
        tabPraesenz.SuspendLayout();
        tabBle.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // menuStrip
        menuStrip.Items.AddRange(new ToolStripItem[] { mnuDatei, mnuHilfe });
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name = "menuStrip";
        menuStrip.Size = new Size(710, 24);
        menuStrip.TabIndex = 0;

        mnuDatei.DropDownItems.AddRange(new ToolStripItem[] { mnuLaden, mnuSpeichern, mnuTrennstrich, mnuSchliessen });
        mnuDatei.Name = "mnuDatei";
        mnuDatei.Size = new Size(46, 20);
        mnuDatei.Text = "Datei";

        mnuLaden.Name = "mnuLaden";
        mnuLaden.Size = new Size(126, 22);
        mnuLaden.Text = "Laden";
        mnuLaden.Click += mnuLaden_Click;

        mnuSpeichern.Name = "mnuSpeichern";
        mnuSpeichern.Size = new Size(126, 22);
        mnuSpeichern.Text = "Speichern";
        mnuSpeichern.Click += mnuSpeichern_Click;

        mnuTrennstrich.Name = "mnuTrennstrich";
        mnuTrennstrich.Size = new Size(123, 6);

        mnuSchliessen.Name = "mnuSchliessen";
        mnuSchliessen.Size = new Size(126, 22);
        mnuSchliessen.Text = "Schließen";
        mnuSchliessen.Click += mnuSchliessen_Click;

        mnuHilfe.DropDownItems.AddRange(new ToolStripItem[] { mnuDokumentation, mnuLogOeffnen });
        mnuHilfe.Name = "mnuHilfe";
        mnuHilfe.Size = new Size(44, 20);
        mnuHilfe.Text = "Hilfe";

        mnuDokumentation.Name = "mnuDokumentation";
        mnuDokumentation.Size = new Size(170, 22);
        mnuDokumentation.Text = "Dokumentation öffnen";
        mnuDokumentation.Click += mnuDokumentation_Click;

        mnuLogOeffnen.Name = "mnuLogOeffnen";
        mnuLogOeffnen.Size = new Size(170, 22);
        mnuLogOeffnen.Text = "Log öffnen";
        mnuLogOeffnen.Click += mnuLogOeffnen_Click;

        // tabControl
        tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tabControl.Controls.Add(tabAllgemein);
        tabControl.Controls.Add(tabPraesenz);
        tabControl.Controls.Add(tabBle);
        tabControl.Controls.Add(tabZuordnung);
        tabControl.Location = new Point(0, 24);
        tabControl.Name = "tabControl";
        tabControl.SelectedIndex = 0;
        tabControl.Size = new Size(710, 413);
        tabControl.TabIndex = 1;

        // tabAllgemein
        tabAllgemein.Controls.Add(grpAzure);
        tabAllgemein.Controls.Add(grpPolling);
        tabAllgemein.Location = new Point(4, 24);
        tabAllgemein.Name = "tabAllgemein";
        tabAllgemein.Padding = new Padding(4);
        tabAllgemein.Size = new Size(702, 385);
        tabAllgemein.TabIndex = 0;
        tabAllgemein.Text = "Allgemein";

        grpAzure.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpAzure.Controls.Add(lblClientId);
        grpAzure.Controls.Add(txtClientId);
        grpAzure.Controls.Add(lblTenantId);
        grpAzure.Controls.Add(txtTenantId);
        grpAzure.Location = new Point(8, 8);
        grpAzure.Name = "grpAzure";
        grpAzure.Size = new Size(686, 90);
        grpAzure.TabIndex = 0;
        grpAzure.TabStop = false;
        grpAzure.Text = "Azure AD / Entra ID";

        lblClientId.AutoSize = true;
        lblClientId.Location = new Point(10, 24);
        lblClientId.Name = "lblClientId";
        lblClientId.TabIndex = 0;
        lblClientId.Text = "Client-ID:";

        txtClientId.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtClientId.Location = new Point(140, 21);
        txtClientId.Name = "txtClientId";
        txtClientId.Size = new Size(528, 23);
        txtClientId.TabIndex = 1;

        lblTenantId.AutoSize = true;
        lblTenantId.Location = new Point(10, 56);
        lblTenantId.Name = "lblTenantId";
        lblTenantId.TabIndex = 2;
        lblTenantId.Text = "Tenant-ID:";

        txtTenantId.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtTenantId.Location = new Point(140, 53);
        txtTenantId.Name = "txtTenantId";
        txtTenantId.Size = new Size(528, 23);
        txtTenantId.TabIndex = 3;

        grpPolling.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpPolling.Controls.Add(lblGraphInterval);
        grpPolling.Controls.Add(nudGraphInterval);
        grpPolling.Controls.Add(lblBleRetry);
        grpPolling.Controls.Add(nudBleRetry);
        grpPolling.Controls.Add(lblBrightnessCap);
        grpPolling.Controls.Add(nudBrightnessCap);
        grpPolling.Location = new Point(8, 108);
        grpPolling.Name = "grpPolling";
        grpPolling.Size = new Size(686, 116);
        grpPolling.TabIndex = 1;
        grpPolling.TabStop = false;
        grpPolling.Text = "Polling & Helligkeit";

        lblGraphInterval.AutoSize = true;
        lblGraphInterval.Location = new Point(10, 25);
        lblGraphInterval.Name = "lblGraphInterval";
        lblGraphInterval.TabIndex = 0;
        lblGraphInterval.Text = "Teams-Abfrageintervall (Sek.):";

        nudGraphInterval.Location = new Point(200, 22);
        nudGraphInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
        nudGraphInterval.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
        nudGraphInterval.Name = "nudGraphInterval";
        nudGraphInterval.Size = new Size(80, 23);
        nudGraphInterval.TabIndex = 1;
        nudGraphInterval.Value = new decimal(new int[] { 30, 0, 0, 0 });

        lblBleRetry.AutoSize = true;
        lblBleRetry.Location = new Point(10, 55);
        lblBleRetry.Name = "lblBleRetry";
        lblBleRetry.TabIndex = 2;
        lblBleRetry.Text = "BLE Retry-Intervall (Sek.):";

        nudBleRetry.Location = new Point(200, 52);
        nudBleRetry.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
        nudBleRetry.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
        nudBleRetry.Name = "nudBleRetry";
        nudBleRetry.Size = new Size(80, 23);
        nudBleRetry.TabIndex = 3;
        nudBleRetry.Value = new decimal(new int[] { 10, 0, 0, 0 });

        lblBrightnessCap.AutoSize = true;
        lblBrightnessCap.Location = new Point(10, 85);
        lblBrightnessCap.Name = "lblBrightnessCap";
        lblBrightnessCap.TabIndex = 4;
        lblBrightnessCap.Text = "Maximale Helligkeit (0–100 %):";

        nudBrightnessCap.Increment = new decimal(new int[] { 5, 0, 0, 0 });
        nudBrightnessCap.Location = new Point(200, 82);
        nudBrightnessCap.Name = "nudBrightnessCap";
        nudBrightnessCap.Size = new Size(80, 23);
        nudBrightnessCap.TabIndex = 5;
        nudBrightnessCap.Value = new decimal(new int[] { 60, 0, 0, 0 });

        // tabPraesenz
        tabPraesenz.Controls.Add(lblPresenceHint);
        tabPraesenz.Controls.Add(chkLivePreview);
        tabPraesenz.Controls.Add(lblCurrPresenceCaption);
        tabPraesenz.Controls.Add(lblCurrPresence);
        tabPraesenz.Controls.Add(btnFetchNow);
        tabPraesenz.Controls.Add(lblOverrideCaption);
        tabPraesenz.Controls.Add(cmbOverride);
        tabPraesenz.Controls.Add(btnApplyOverride);
        tabPraesenz.Controls.Add(lblActiveOverride);
        tabPraesenz.Controls.Add(chkKeepOverride);
        tabPraesenz.Controls.Add(pnlPresence);
        tabPraesenz.Location = new Point(4, 24);
        tabPraesenz.Name = "tabPraesenz";
        tabPraesenz.Size = new Size(702, 385);
        tabPraesenz.TabIndex = 1;
        tabPraesenz.Text = "Präsenz";

        lblPresenceHint.AutoSize = true;
        lblPresenceHint.Location = new Point(6, 6);
        lblPresenceHint.Name = "lblPresenceHint";
        lblPresenceHint.TabIndex = 0;
        lblPresenceHint.Text = "Farbe per Klick auf ■ oder R/G/B ändern.";

        chkLivePreview.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkLivePreview.AutoSize = true;
        chkLivePreview.Checked = true;
        chkLivePreview.CheckState = CheckState.Checked;
        chkLivePreview.Location = new Point(590, 5);
        chkLivePreview.Name = "chkLivePreview";
        chkLivePreview.TabIndex = 2;
        chkLivePreview.Text = "Live-Vorschau";

        // Präsenz toolbar row 1: Teams status + fetch
        lblCurrPresenceCaption.AutoSize = true;
        lblCurrPresenceCaption.Location = new Point(6, 30);
        lblCurrPresenceCaption.Name = "lblCurrPresenceCaption";
        lblCurrPresenceCaption.Text = "Teams:";

        lblCurrPresence.AutoSize = false;
        lblCurrPresence.Location = new Point(56, 27);
        lblCurrPresence.Size = new Size(130, 20);
        lblCurrPresence.Name = "lblCurrPresence";
        lblCurrPresence.Text = "—";
        lblCurrPresence.Font = new Font(Font, FontStyle.Bold);

        btnFetchNow.Location = new Point(192, 26);
        btnFetchNow.Name = "btnFetchNow";
        btnFetchNow.Size = new Size(100, 22);
        btnFetchNow.TabIndex = 3;
        btnFetchNow.Text = "Jetzt abrufen";

        // Präsenz toolbar row 1: Override selector
        lblOverrideCaption.AutoSize = true;
        lblOverrideCaption.Location = new Point(302, 30);
        lblOverrideCaption.Name = "lblOverrideCaption";
        lblOverrideCaption.Text = "Override:";

        cmbOverride.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbOverride.Location = new Point(362, 27);
        cmbOverride.Name = "cmbOverride";
        cmbOverride.Size = new Size(130, 23);
        cmbOverride.TabIndex = 4;

        btnApplyOverride.Location = new Point(498, 26);
        btnApplyOverride.Name = "btnApplyOverride";
        btnApplyOverride.Size = new Size(80, 22);
        btnApplyOverride.TabIndex = 5;
        btnApplyOverride.Text = "Anwenden";

        // Präsenz toolbar row 2: active override info
        lblActiveOverride.AutoSize = false;
        lblActiveOverride.Location = new Point(6, 52);
        lblActiveOverride.Size = new Size(460, 16);
        lblActiveOverride.Name = "lblActiveOverride";
        lblActiveOverride.Text = "Kein Override aktiv";
        lblActiveOverride.ForeColor = SystemColors.GrayText;

        chkKeepOverride.AutoSize = true;
        chkKeepOverride.Location = new Point(476, 50);
        chkKeepOverride.Name = "chkKeepOverride";
        chkKeepOverride.TabIndex = 6;
        chkKeepOverride.Text = "Override beibehalten";

        pnlPresence.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        pnlPresence.AutoScroll = true;
        pnlPresence.Location = new Point(6, 72);
        pnlPresence.Name = "pnlPresence";
        pnlPresence.Size = new Size(690, 307);
        pnlPresence.TabIndex = 7;

        // tabBle
        tabBle.Controls.Add(lblBleInfo);
        tabBle.Controls.Add(lblBleDevice);
        tabBle.Controls.Add(btnRescan);
        tabBle.Controls.Add(btnStopScan);
        tabBle.Location = new Point(4, 24);
        tabBle.Name     = "tabBle";
        tabBle.Size     = new Size(702, 385);
        tabBle.TabIndex = 2;
        tabBle.Text     = "BLE-Gerät";

        lblBleInfo.AutoSize = true;
        lblBleInfo.Location = new Point(6, 6);
        lblBleInfo.Name     = "lblBleInfo";
        lblBleInfo.TabIndex = 0;
        lblBleInfo.Text     = "Jeder Laptop verbindet sich mit genau einem BusyLight-Gerät.";

        lblBleDevice.AutoSize = false;
        lblBleDevice.Font     = new Font(Font, FontStyle.Bold);
        lblBleDevice.Location = new Point(6, 32);
        lblBleDevice.Name     = "lblBleDevice";
        lblBleDevice.Size     = new Size(650, 24);
        lblBleDevice.TabIndex = 1;
        lblBleDevice.Text     = "Kein Gerät ausgewählt";

        btnRescan.Location = new Point(6, 68);
        btnRescan.Name     = "btnRescan";
        btnRescan.Size     = new Size(200, 28);
        btnRescan.TabIndex = 2;
        btnRescan.Text     = "Neues Gerät suchen…";
        btnRescan.Click   += btnRescan_Click;

        btnStopScan.Location = new Point(214, 68);
        btnStopScan.Name     = "btnStopScan";
        btnStopScan.Size     = new Size(180, 28);
        btnStopScan.TabIndex = 3;
        btnStopScan.Text     = "Suche unterbrechen";
        btnStopScan.Enabled  = false;

        // tabZuordnung
        tabZuordnung.Controls.Add(lblZuordnungHint);
        tabZuordnung.Controls.Add(pnlZuordnung);
        tabZuordnung.Location = new Point(4, 24);
        tabZuordnung.Name = "tabZuordnung";
        tabZuordnung.Size = new Size(702, 385);
        tabZuordnung.TabIndex = 3;
        tabZuordnung.Text = "Zuordnung";

        lblZuordnungHint.AutoSize = true;
        lblZuordnungHint.Location = new Point(6, 6);
        lblZuordnungHint.Name = "lblZuordnungHint";
        lblZuordnungHint.TabIndex = 0;
        lblZuordnungHint.Text = "Welches LED-Profil soll bei jedem Teams-Status angezeigt werden?";

        pnlZuordnung.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        pnlZuordnung.AutoScroll = true;
        pnlZuordnung.Location = new Point(6, 28);
        pnlZuordnung.Name = "pnlZuordnung";
        pnlZuordnung.Size = new Size(688, 350);
        pnlZuordnung.TabIndex = 1;

        // statusStrip
        statusStrip.Items.AddRange(new ToolStripItem[]
            { sslTeams, sslSep, sslBle, sslSep2, sslProtocol, sslSep3, sslVersion });
        statusStrip.Location = new Point(0, 437);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(710, 22);
        statusStrip.TabIndex = 2;

        sslTeams.Name    = "sslTeams";
        sslTeams.Text    = "Teams: —";

        sslSep.Name      = "sslSep";
        sslSep.Text      = " | ";
        sslSep.ForeColor = SystemColors.GrayText;

        sslBle.Name      = "sslBle";
        sslBle.Text      = "BLE: Suche…";
        sslBle.Spring    = true;
        sslBle.TextAlign = ContentAlignment.MiddleLeft;

        sslSep2.Name      = "sslSep2";
        sslSep2.Text      = " | ";
        sslSep2.ForeColor = SystemColors.GrayText;

        sslProtocol.Name = "sslProtocol";
        sslProtocol.Text = "Protokoll: —";

        sslSep3.Name      = "sslSep3";
        sslSep3.Text      = " | ";
        sslSep3.ForeColor = SystemColors.GrayText;

        sslVersion.Name = "sslVersion";
        sslVersion.Text = "App v—";

        // SettingsForm
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(710, 459);
        Controls.Add(menuStrip);
        Controls.Add(tabControl);
        Controls.Add(statusStrip);
        MainMenuStrip = menuStrip;
        MinimumSize = new Size(720, 480);
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "BusyLight — Einstellungen";

        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        tabControl.ResumeLayout(false);
        tabAllgemein.ResumeLayout(false);
        grpAzure.ResumeLayout(false);
        grpAzure.PerformLayout();
        grpPolling.ResumeLayout(false);
        grpPolling.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)nudGraphInterval).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudBleRetry).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudBrightnessCap).EndInit();
        tabPraesenz.ResumeLayout(false);
        tabPraesenz.PerformLayout();
        tabBle.ResumeLayout(false);
        tabBle.PerformLayout();
        tabZuordnung.ResumeLayout(false);
        tabZuordnung.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    // ── Designer-managed controls ─────────────────────────────────────────────

    private System.Windows.Forms.MenuStrip              menuStrip            = default!;
    private System.Windows.Forms.ToolStripMenuItem      mnuDatei             = default!;
    private System.Windows.Forms.ToolStripMenuItem      mnuLaden             = default!;
    private System.Windows.Forms.ToolStripMenuItem      mnuSpeichern         = default!;
    private System.Windows.Forms.ToolStripSeparator     mnuTrennstrich       = default!;
    private System.Windows.Forms.ToolStripMenuItem      mnuSchliessen        = default!;
    private System.Windows.Forms.ToolStripMenuItem      mnuHilfe             = default!;
    private System.Windows.Forms.ToolStripMenuItem      mnuDokumentation     = default!;
    private System.Windows.Forms.ToolStripMenuItem      mnuLogOeffnen        = default!;

    private System.Windows.Forms.TabControl             tabControl           = default!;
    private System.Windows.Forms.TabPage                tabAllgemein         = default!;
    private System.Windows.Forms.TabPage                tabPraesenz          = default!;
    private System.Windows.Forms.TabPage                tabBle               = default!;

    private System.Windows.Forms.GroupBox               grpAzure             = default!;
    private System.Windows.Forms.Label                  lblClientId          = default!;
    private System.Windows.Forms.TextBox                txtClientId          = default!;
    private System.Windows.Forms.Label                  lblTenantId          = default!;
    private System.Windows.Forms.TextBox                txtTenantId          = default!;

    private System.Windows.Forms.GroupBox               grpPolling           = default!;
    private System.Windows.Forms.Label                  lblGraphInterval     = default!;
    private System.Windows.Forms.NumericUpDown          nudGraphInterval     = default!;
    private System.Windows.Forms.Label                  lblBleRetry          = default!;
    private System.Windows.Forms.NumericUpDown          nudBleRetry          = default!;
    private System.Windows.Forms.Label                  lblBrightnessCap     = default!;
    private System.Windows.Forms.NumericUpDown          nudBrightnessCap     = default!;

    private System.Windows.Forms.Label                  lblPresenceHint      = default!;
    private System.Windows.Forms.CheckBox               chkLivePreview       = default!;
    private System.Windows.Forms.Label                  lblCurrPresenceCaption = default!;
    private System.Windows.Forms.Label                  lblCurrPresence      = default!;
    private System.Windows.Forms.Button                 btnFetchNow          = default!;
    private System.Windows.Forms.Label                  lblOverrideCaption   = default!;
    private System.Windows.Forms.ComboBox               cmbOverride          = default!;
    private System.Windows.Forms.Button                 btnApplyOverride     = default!;
    private System.Windows.Forms.Label                  lblActiveOverride    = default!;
    private System.Windows.Forms.CheckBox               chkKeepOverride      = default!;
    private System.Windows.Forms.Panel                  pnlPresence          = default!;

    private System.Windows.Forms.Label                  lblBleInfo           = default!;
    private System.Windows.Forms.Label                  lblBleDevice         = default!;
    private System.Windows.Forms.Button                 btnRescan            = default!;
    private System.Windows.Forms.Button                 btnStopScan          = default!;

    private System.Windows.Forms.TabPage                tabZuordnung         = default!;
    private System.Windows.Forms.Label                  lblZuordnungHint     = default!;
    private System.Windows.Forms.Panel                  pnlZuordnung         = default!;

    private System.Windows.Forms.StatusStrip            statusStrip          = default!;
    private System.Windows.Forms.ToolStripStatusLabel   sslTeams             = default!;
    private System.Windows.Forms.ToolStripStatusLabel   sslSep               = default!;
    private System.Windows.Forms.ToolStripStatusLabel   sslBle               = default!;
    private System.Windows.Forms.ToolStripStatusLabel   sslSep2              = default!;
    private System.Windows.Forms.ToolStripStatusLabel   sslProtocol          = default!;
    private System.Windows.Forms.ToolStripStatusLabel   sslSep3              = default!;
    private System.Windows.Forms.ToolStripStatusLabel   sslVersion           = default!;
}
