using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace Drex;

public enum TrayState { Unknown, Protected, Warning, Disabled, Scanning }

sealed class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Surf;
    public override Color ImageMarginGradientBegin => Theme.Surf;
    public override Color ImageMarginGradientMiddle => Theme.Surf;
    public override Color ImageMarginGradientEnd => Theme.Surf;
    public override Color MenuItemSelected => Theme.Surf2;
    public override Color MenuItemSelectedGradientBegin => Theme.Surf2;
    public override Color MenuItemSelectedGradientEnd => Theme.Surf2;
    public override Color MenuItemBorder => Theme.Surf2;
    public override Color MenuBorder => Theme.Surf2;
    public override Color SeparatorDark => Theme.Surf2;
    public override Color SeparatorLight => Theme.Surf2;
}

public sealed partial class MainForm : Form
{
    NotifyIcon? _tray;
    ToolStripMenuItem? _miStatus;
    static readonly Dictionary<TrayState, Icon> _icons = new();
    TrayState _trayState = TrayState.Unknown;
    bool _exiting, _trayChecking, _hintShown;
    Func<Task>? _secQuick, _secFull, _secUpdate, _secCheck;
    Func<string, Task>? _secCustom;
    string? _pendingUsb;

    // Draws a small status icon in code (no extra files, created once per state and cached).
    static Icon IconFor(TrayState s)
    {
        if (_icons.TryGetValue(s, out var cached)) return cached;
        var col = s switch { TrayState.Protected => Theme.Good, TrayState.Warning => Theme.Warn, TrayState.Disabled => Theme.Bad, TrayState.Scanning => Theme.Accent, _ => Theme.Muted };
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using (var logo = AppIcon.Get(32)) g.DrawIcon(logo, new Rectangle(0, 0, 32, 32));
            using var ring = new SolidBrush(Color.FromArgb(11, 13, 18)); g.FillEllipse(ring, 15, 15, 17, 17);
            using var dot = new SolidBrush(col); g.FillEllipse(dot, 17, 17, 13, 13);
        }
        var icon = Icon.FromHandle(bmp.GetHicon());   // 5 cached icons for the process lifetime
        _icons[s] = icon;
        return icon;
    }

    ToolStripMenuItem Item(string text, Action a)
    {
        var mi = new ToolStripMenuItem(text) { ForeColor = Theme.Txt, BackColor = Theme.Surf };
        mi.Click += (_, _) => { try { a(); } catch { } };
        return mi;
    }

    void InitTray()
    {
        _tray = new NotifyIcon { Icon = IconFor(TrayState.Unknown), Text = "DREX — protection not verified yet", Visible = true };
        var m = new ContextMenuStrip { BackColor = Theme.Surf, ForeColor = Theme.Txt, ShowImageMargin = false, Font = Theme.F(9.5f), Renderer = new ToolStripProfessionalRenderer(new DarkColors()) };
        var open = Item("Open DREX Dashboard", ShowDashboard); open.Font = Theme.F(9.5f, true);
        _miStatus = new ToolStripMenuItem("Protection: not verified yet") { Enabled = false, ForeColor = Theme.Muted };
        m.Items.Add(open);
        m.Items.Add(_miStatus);
        m.Items.Add(Item("Check protection now", () => _ = TrayVerify(true)));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item("Quick virus scan (Windows Defender)", () => SecurityAction(0)));
        m.Items.Add(Item("Full system scan (Windows Defender)", () => SecurityAction(1)));
        m.Items.Add(Item("Update virus definitions", () => SecurityAction(2)));
        m.Items.Add(Item("Clean junk files now", () => { ShowDashboard(); Go("Dashboard"); _ = ScanThenClean(); }));
        m.Items.Add(Item("Open Windows Security", () => { try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); } catch { } }));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item("Activity log", () => { ShowDashboard(); Go("History"); }));
        m.Items.Add(Item("Settings", () => { ShowDashboard(); Go("Settings"); }));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item("Exit DREX", ExitApp));
        _tray.ContextMenuStrip = m;
        _tray.BalloonTipClicked += (_, _) => { var p = _pendingUsb; _pendingUsb = null; if (p != null) ScanPath(p); };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };
    }

    public void ShowDashboard()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    void HideToTray()
    {
        Hide();
        if (!_hintShown && _tray != null)
        {
            _hintShown = true;
            _tray.ShowBalloonTip(3000, "DREX is still running", "Right-click the tray icon to scan, update or exit.", ToolTipIcon.None);
        }
    }

    void ExitApp() { _exiting = true; Close(); }

    void Cleanup()
    {
        _cts?.Cancel(); _ramTimer.Stop(); _s.Save();
        try { _scanProc?.Kill(true); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
    }

    public void Notify(string text, ToolTipIcon icon) { try { _tray?.ShowBalloonTip(5000, "DREX PC CLEANER", text, icon == ToolTipIcon.Warning ? ToolTipIcon.Warning : ToolTipIcon.None); } catch { } }

    async void SecurityAction(int which)
    {
        try
        {
            ShowDashboard(); Go("Security"); await WaitIdle();
            var f = which == 0 ? _secQuick : which == 1 ? _secFull : _secUpdate;
            if (f != null) await f();
        }
        catch { }
    }

    async Task WaitIdle() { for (int i = 0; i < 80 && _busy; i++) await Task.Delay(250); }

    async void ScanPath(string path)
    {
        try
        {
            ShowDashboard(); Go("Security"); await WaitIdle();
            if (_secCustom != null) await _secCustom(path);
        }
        catch { }
    }

    // USB drives: Windows sends WM_DEVICECHANGE when a volume arrives, so no polling or watcher thread is needed.
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x0219 && (int)m.WParam == 0x8000 && m.LParam != IntPtr.Zero)
        {
            try
            {
                if (Marshal.ReadInt32(m.LParam, 4) != 2) return;           // DBT_DEVTYP_VOLUME
                int mask = Marshal.ReadInt32(m.LParam, 12); short flags = Marshal.ReadInt16(m.LParam, 16);
                if ((flags & 2) != 0) return;                              // network share
                for (int i = 0; i < 26; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    string root = ((char)('A' + i)).ToString() + @":\";
                    BeginInvoke(new Action(() => UsbArrived(root)));
                }
            }
            catch { }
        }
    }

    void UsbArrived(string root)
    {
        try
        {
            if (new DriveInfo(root).DriveType != DriveType.Removable) return;
            History.Add("USB drive connected: " + root);
            if (_s.UsbAutoScan) ScanPath(root);
            else { _pendingUsb = root; Notify($"USB drive {root} connected. Click here to scan it with Windows Defender.", ToolTipIcon.None); }
        }
        catch { }
    }

    async Task ScanThenClean()
    {
        await ScanSafe();
        if (_lastTotal > 0) await CleanSafe();
    }

    public void SetTray(TrayState s, string text)
    {
        if (_tray == null) return;
        _trayState = s;
        _tray.Icon = IconFor(s);
        var t = "DREX — " + text;
        _tray.Text = t.Length > 63 ? t[..63] : t;
        if (_miStatus != null) _miStatus.Text = "Protection: " + text;
    }

    // The tray icon only says "Protected" when Windows Defender's real-time protection is verified ON and definitions are recent.
    public void ApplyReport(SecurityReport rep)
    {
        TrayState s; string txt;
        if (!rep.DefenderKnown) { s = TrayState.Warning; txt = "cannot verify Windows Defender"; }
        else if (!rep.Av || !rep.Rtp) { s = TrayState.Disabled; txt = "real-time protection is OFF"; }
        else if (!rep.DefsOk || rep.Checks.Any(c => c.Status == "Risk")) { s = TrayState.Warning; txt = "attention needed — see Security tab"; }
        else { s = TrayState.Protected; txt = "Defender protection verified"; }
        txt += $" ({DateTime.Now:HH:mm})";
        var prev = _trayState;
        SetTray(s, txt);
        if (prev == TrayState.Protected && (s == TrayState.Disabled || s == TrayState.Warning))
            Notify("Protection status changed: " + txt, ToolTipIcon.Warning);
    }

    async Task TrayVerify(bool announce = false)
    {
        if (_trayChecking || _tray == null) return;
        _trayChecking = true;
        try
        {
            var rep = await Task.Run(() => Security.CheckAll());
            ApplyReport(rep);
            if (announce) Notify(_miStatus?.Text ?? "Checked", _trayState == TrayState.Protected ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
        catch { SetTray(TrayState.Unknown, "could not verify protection"); }
        finally { _trayChecking = false; }
    }
}
