using System.Diagnostics;
using Microsoft.Win32;
namespace Drex;

public sealed partial class MainForm : Form
{
    readonly Settings _s = Settings.Load();
    readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly Dictionary<string, Control> _pages = new();
    readonly Dictionary<string, NavItem> _nav = new();
    readonly System.Windows.Forms.Timer _ramTimer = new() { Interval = 5000 };
    static readonly bool IsAdmin = Native.IsAdmin();
    CancellationTokenSource? _cts;
    bool _busy, _wasMin;

    // dashboard
    Label _lblReclaim = null!, _lblStatus = null!, _lblRamTxt = null!, _lblDisk = null!, _lblDiskSub = null!;
    Ring _ring = null!; DarkBar _diskBar = null!;
    GButton _btnScan = null!, _btnClean = null!, _btnCancel = null!;
    DarkList _lvCats = null!;
    List<Category> _lastCats = new();
    long _lastTotal;
    TextBox? _historyBox;

    static readonly (string key, string glyph)[] Items =
    {
        ("Dashboard", "\uE80F"), ("Security", "\uE72E"), ("Threats", "\uE7BA"), ("Deep Clean", "\uE74D"), ("Startup", "\uE7E8"),
        ("Large Files", "\uE8B7"), ("Duplicates", "\uE8C8"), ("Disk Usage", "\uEDA2"), ("Web Blocker", "\uE783"),
        ("Backup", "\uE74E"), ("History", "\uE81C"), ("Settings", "\uE713"),
    };

    readonly bool _startHidden;

    public MainForm(bool startHidden = false)
    {
        _startHidden = startHidden;
        if (startHidden) { Opacity = 0; ShowInTaskbar = false; }
        Text = "DREX PC CLEANER"; BackColor = Theme.Bg; ForeColor = Theme.Txt; Font = Theme.F(9.5f);
        ClientSize = new Size(1120, 720); MinimumSize = new Size(1000, 680); StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Get(48);
        DoubleBuffered = true;

        var side = new Panel { Dock = DockStyle.Left, Width = 212, BackColor = Theme.Surf };
        var navHost = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 96, 0, 0), BackColor = Theme.Surf };
        var foot = L(IsAdmin ? "● Administrator" : "● Standard user", 8.5f, IsAdmin ? Theme.Good : Theme.Muted, false, Theme.Surf);
        foot.Dock = DockStyle.Bottom; foot.AutoSize = false; foot.Height = 34; foot.Padding = new Padding(22, 0, 0, 0); foot.TextAlign = ContentAlignment.MiddleLeft;
        var brand = L("DREX", 24, Theme.Violet, true, Theme.Surf); brand.Location = new Point(20, 18);
        var sub = L("PC CLEANER", 8.5f, Theme.Muted, true, Theme.Surf); sub.Location = new Point(24, 62);
        side.Controls.Add(navHost); side.Controls.Add(foot); side.Controls.Add(brand); side.Controls.Add(sub);
        foreach (var (k, g) in Items)
        {
            var n = new NavItem(k, g); var key = k;
            n.Click += (_, _) => Go(key);
            _nav[k] = n; navHost.Controls.Add(n);
        }
        Controls.Add(_content); Controls.Add(side);

        _ramTimer.Tick += (_, _) => UpdateSystem();
        Shown += async (_, _) =>
        {
            Go("Dashboard"); StartRamTimer();
            if (_startHidden) { Hide(); Opacity = 1; ShowInTaskbar = true; }
            await RunLaunchFlow();
            await TrayVerify();
        };
        FormClosing += (_, e) =>
        {
            if (!_exiting && _tray != null && e.CloseReason == CloseReason.UserClosing)
            {
                if (!_s.CloseAsked)
                {
                    var r = MessageBox.Show(this, "Keep DREX in the system tray?\n\nYes  –  hide to the tray (quick access and protection status)\nNo  –  exit DREX completely\n\nYou can change this later in Settings.", "DREX PC CLEANER", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                    _s.CloseAsked = true; _s.CloseToTray = r == DialogResult.Yes; _s.Save();
                }
                if (_s.CloseToTray) { e.Cancel = true; HideToTray(); return; }
            }
            Cleanup();
        };
        VisibleChanged += (_, _) => { if (!Visible) _ramTimer.Stop(); else StartRamTimer(); };
        SizeChanged += (_, _) =>
        {
            bool min = WindowState == FormWindowState.Minimized;
            if (min && _s.CloseToTray && _s.CloseAsked && _tray != null) HideToTray();
            if (min == _wasMin) return;
            _wasMin = min;
            if (min) _ramTimer.Stop(); else StartRamTimer();
        };
        InitTray();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitle(Handle); }

    // ---------- shared helpers ----------
    static Label L(string text, float size = 9.5f, Color? color = null, bool bold = false, Color? bg = null) =>
        new() { Text = text, AutoSize = true, Font = Theme.F(size, bold), ForeColor = color ?? Theme.Silver, BackColor = bg ?? Theme.Bg, UseMnemonic = false };

    static Control Header(string title, string sub)
    {
        var p = new Panel { Height = 66, BackColor = Theme.Bg };
        var t = L(title, 21, Theme.Txt, true); t.Location = new Point(0, 0);
        var s = L(sub, 9.5f, Theme.Muted); s.Location = new Point(2, 40);
        p.Controls.Add(t); p.Controls.Add(s);
        return p;
    }

    static FlowLayoutPanel Bar(params Control[] cs)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Theme.Bg, Margin = Padding.Empty };
        f.Controls.AddRange(cs);
        return f;
    }

    static Label StatusLabel(string t) => new()
    {
        Text = t, AutoSize = false, Height = 26, ForeColor = Theme.Muted, BackColor = Theme.Bg, Font = Theme.F(9.5f),
        AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false
    };

    static Control Stack(params (Control c, bool fill)[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Bg, Padding = new Padding(30, 24, 30, 16) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (c, fill) in rows)
        {
            bool auto = c is FlowLayoutPanel fl && fl.AutoSize;
            t.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : auto ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, c.Height + 12));
            c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 0, 0, 12);
            t.Controls.Add(c);
        }
        return t;
    }

    static TextBox Input(string text, int w, string placeholder = "") => new()
    {
        Text = text, Width = w, BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.F(10f), Margin = new Padding(0, 9, 8, 0), PlaceholderText = placeholder
    };

    static NumericUpDown Num(int min, int max, int val, int w = 84) => new()
    {
        Minimum = min, Maximum = max, Value = Math.Clamp(val, min, max), Width = w, BackColor = Theme.Surf2, ForeColor = Theme.Txt,
        BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(10f), Margin = new Padding(0, 9, 8, 0)
    };

    static Label Cap(string t) { var l = L(t, 9.5f, Theme.Muted); l.Margin = new Padding(6, 14, 6, 0); return l; }

    void Browse(TextBox tb) { using var d = new FolderBrowserDialog { SelectedPath = tb.Text }; if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath; }

    bool Begin(out CancellationToken ct)
    {
        ct = default;
        if (_busy) { MessageBox.Show(this, "Another task is still running. Wait for it or cancel it first.", "DREX PC CLEANER"); return false; }
        _busy = true; _cts = new CancellationTokenSource(); ct = _cts.Token; return true;
    }
    void End() { _busy = false; _cts = null; }

    TimeSpan MinAge => TimeSpan.FromHours(Math.Max(0, _s.MinAgeHours));

    // ---------- navigation ----------
    void Go(string name)
    {
        foreach (var kv in _nav) kv.Value.Selected = kv.Key == name;
        if (!_pages.TryGetValue(name, out var page))
        {
            page = name switch
            {
                "Dashboard" => BuildDashboard(), "Security" => BuildSecurity(), "Threats" => BuildThreats(), "Deep Clean" => BuildDeep(), "Startup" => BuildStartup(),
                "Large Files" => BuildLarge(), "Duplicates" => BuildDupes(), "Disk Usage" => BuildDisk(), "Web Blocker" => BuildWeb(),
                "Backup" => BuildBackup(), "History" => BuildHistory(), _ => BuildSettings()
            };
            page.Dock = DockStyle.Fill; _pages[name] = page; _content.Controls.Add(page);
        }
        page.BringToFront();
        if (name == "History" && _historyBox != null) { _historyBox.Text = History.ReadAll(); _historyBox.SelectionStart = _historyBox.TextLength; _historyBox.ScrollToCaret(); }
    }

    // ---------- Dashboard ----------
    Control BuildDashboard()
    {
        var c1 = new Card { Accent = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        var c1cap = L("RECLAIMABLE  ·  SAFE TEMP", 8.5f, Theme.Muted, true, Theme.Surf); c1cap.Location = new Point(20, 16);
        _lblReclaim = L("—", 30, Theme.Violet, true, Theme.Surf); _lblReclaim.Location = new Point(18, 38);
        var c1sub = L("Only files older than your minimum age. Locked files are skipped.", 8.5f, Theme.Muted, false, Theme.Surf);
        c1sub.MaximumSize = new Size(210, 0); c1sub.Location = new Point(20, 110);
        c1.Controls.AddRange(new Control[] { c1cap, _lblReclaim, c1sub });

        var c2 = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        var c2cap = L("MEMORY", 8.5f, Theme.Muted, true, Theme.Surf); c2cap.Location = new Point(20, 16);
        _ring = new Ring { Size = new Size(104, 104), Location = new Point(12, 44), Big = "—", Small = "used" };
        _lblRamTxt = L("", 9.5f, Theme.Silver, false, Theme.Surf); _lblRamTxt.Location = new Point(124, 62); _lblRamTxt.MaximumSize = new Size(110, 0);
        c2.Controls.AddRange(new Control[] { c2cap, _ring, _lblRamTxt });

        var c3 = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var c3cap = L("SYSTEM DRIVE", 8.5f, Theme.Muted, true, Theme.Surf); c3cap.Location = new Point(20, 16);
        _lblDisk = L("—", 18, Theme.Txt, true, Theme.Surf); _lblDisk.Location = new Point(18, 42);
        _diskBar = new DarkBar { Location = new Point(20, 98), Width = 180 };
        _lblDiskSub = L("", 8.5f, Theme.Muted, false, Theme.Surf); _lblDiskSub.Location = new Point(20, 116);
        c3.SizeChanged += (_, _) => _diskBar.Width = Math.Max(60, c3.Width - 40);
        c3.Controls.AddRange(new Control[] { c3cap, _lblDisk, _diskBar, _lblDiskSub });

        var cards = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Height = 176, BackColor = Theme.Bg };
        for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cards.Controls.Add(c1); cards.Controls.Add(c2); cards.Controls.Add(c3);

        _btnScan = new GButton("Scan TEMP"); _btnClean = new GButton("Clean safe files", true); _btnCancel = new GButton("Cancel") { Enabled = false };
        _btnScan.Click += async (_, _) => await ScanSafe();
        _btnClean.Click += async (_, _) => await CleanSafe();
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        _lvCats = new DarkList(("Location", 170), ("Files", 80), ("Size", 110), ("Path", 500));
        _lblStatus = StatusLabel("Starting…");
        return Stack((Header("Dashboard", "Safe cleanup, system health and security at a glance."), false), (cards, false),
            (Bar(_btnScan, _btnClean, _btnCancel), false), (_lvCats, true), (_lblStatus, false));
    }

    void SetDashBusy(bool b) { _btnScan.Enabled = !b; _btnClean.Enabled = !b; _btnCancel.Enabled = b; }

    async Task RunLaunchFlow()
    {
        await ScanSafe();
        if (_s.AutoClean && _lastTotal > 0) await CleanSafe(true);
    }

    async Task ScanSafe(string? keepStatus = null)
    {
        if (!Begin(out var ct)) return;
        SetDashBusy(true); _lblStatus.Text = "Scanning TEMP…"; _lvCats.Items.Clear();
        try
        {
            var cats = Cleaner.SafeCategories(); long total = 0; var age = MinAge;
            foreach (var c in cats)
            {
                var r = await Task.Run(() => Cleaner.Scan(c, age, ct), ct);
                total += r.Bytes;
                _lvCats.Items.Add(new ListViewItem(new[] { c.Name, r.Files.ToString("N0"), Fmt.Size(r.Bytes), string.Join("; ", c.Paths) }));
            }
            _lastCats = cats; _lastTotal = total; _lblReclaim.Text = Fmt.Size(total);
            _lblStatus.Text = keepStatus ?? $"Scan complete. {Fmt.Size(total)} can be safely cleaned (files older than {_s.MinAgeHours} h).";
        }
        catch (OperationCanceledException) { _lblStatus.Text = "Scan cancelled."; }
        catch (Exception ex) { _lblStatus.Text = "Scan error: " + ex.Message; }
        finally { End(); SetDashBusy(false); }
    }

    async Task CleanSafe(bool auto = false)
    {
        if (_lastCats.Count == 0) { _lblStatus.Text = "Run a scan first."; return; }
        if (!Begin(out var ct)) return;
        SetDashBusy(true); _lblStatus.Text = auto ? "Auto Clean running…" : "Cleaning…";
        string msg;
        try
        {
            var total = new CleanResult(); var age = MinAge;
            foreach (var c in _lastCats)
            {
                var r = await Task.Run(() => Cleaner.Clean(c, age, ct), ct);
                total.Bytes += r.Bytes; total.Deleted += r.Deleted; total.Skipped += r.Skipped; total.Errors += r.Errors;
            }
            msg = $"{(auto ? "Auto" : "Manual")} clean: freed {Fmt.Size(total.Bytes)} · {total.Deleted:N0} deleted · {total.Skipped:N0} skipped (in use) · {total.Errors} errors";
        }
        catch (OperationCanceledException) { msg = "Cleanup cancelled (partial results are in History)."; }
        catch (Exception ex) { msg = "Cleanup error: " + ex.Message; }
        History.Add(msg);
        End(); SetDashBusy(false);
        await ScanSafe(msg);
    }

    // ---------- Deep Clean ----------
    Control BuildDeep()
    {
        var analyze = new GButton("Analyze", true); var clean = new GButton("Clean selected") { Enabled = false }; var cancel = new GButton("Cancel") { Enabled = false };
        var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
        Native.DarkScroll(host);
        var status = StatusLabel("Click Analyze to see what can be cleaned. Nothing is deleted until you confirm.");
        var rows = new List<(Toggle t, Category c)>();
        void Fit() { foreach (Control c in host.Controls) c.Width = Math.Max(300, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6); }
        host.SizeChanged += (_, _) => Fit();

        analyze.Click += async (_, _) =>
        {
            if (!Begin(out var ct)) return;
            analyze.Enabled = false; clean.Enabled = false; cancel.Enabled = true; host.Controls.Clear(); rows.Clear(); status.Text = "Analyzing…";
            long total = 0;
            try
            {
                foreach (var c in Cleaner.DeepCategories())
                {
                    var r = await Task.Run(() => Cleaner.Scan(c, TimeSpan.FromHours(1), ct), ct);
                    total += r.Bytes;
                    bool pre = r.Bytes > 0 && !c.Recycle && !c.Name.StartsWith("Recent", StringComparison.Ordinal);
                    var row = MakeRow(c, r, pre, out var tg);
                    rows.Add((tg, c)); host.Controls.Add(row); Fit();
                }
                status.Text = $"Analysis complete — {Fmt.Size(total)} found. Close browsers first for best results.";
            }
            catch (OperationCanceledException) { status.Text = "Cancelled."; }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
            finally { End(); analyze.Enabled = true; clean.Enabled = rows.Count > 0; cancel.Enabled = false; }
        };

        clean.Click += async (_, _) =>
        {
            var sel = rows.Where(x => x.t.Checked).ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "Select at least one category.", "DREX PC CLEANER"); return; }
            if (_s.ConfirmDeepClean && MessageBox.Show(this, "Permanently delete the selected items?\n\n" + string.Join("\n", sel.Select(x => "• " + x.c.Name)),
                "Confirm Deep Clean", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!Begin(out var ct)) return;
            analyze.Enabled = false; clean.Enabled = false; cancel.Enabled = true; long freed = 0; status.Text = "Cleaning…";
            try
            {
                foreach (var (t, c) in sel)
                {
                    var r = await Task.Run(() => Cleaner.Clean(c, TimeSpan.FromHours(1), ct), ct);
                    freed += r.Bytes;
                    History.Add($"Deep clean [{c.Name}]: freed {Fmt.Size(r.Bytes)}, {r.Deleted} deleted, {r.Skipped} skipped, {r.Errors} errors");
                }
                status.Text = $"Deep Clean finished — freed {Fmt.Size(freed)}. Click Analyze to refresh.";
            }
            catch (OperationCanceledException) { status.Text = $"Cancelled after freeing {Fmt.Size(freed)}."; History.Add("Deep clean cancelled"); }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
            finally { End(); analyze.Enabled = true; clean.Enabled = true; cancel.Enabled = false; }
        };
        cancel.Click += (_, _) => _cts?.Cancel();
        return Stack((Header("Deep Clean", "Browser caches, thumbnails, crash dumps, privacy traces and the Recycle Bin."), false),
            (Bar(analyze, clean, cancel), false), (host, true), (status, false));
    }

    static Control MakeRow(Category c, ScanResult r, bool pre, out Toggle tg)
    {
        var row = new Card { Height = 62, Margin = new Padding(0, 0, 0, 8) };
        tg = new Toggle { Location = new Point(18, 17) };
        tg.Checked = pre;
        var name = L(c.Name, 10.5f, Theme.Txt, true, Theme.Surf); name.Location = new Point(82, 10);
        string where = c.Recycle ? "Windows Recycle Bin" : string.Join("; ", c.Paths.Take(1)) + (c.Paths.Length > 1 ? $"  (+{c.Paths.Length - 1} more)" : "");
        var info = L(where, 8.5f, Theme.Muted, false, Theme.Surf); info.Location = new Point(82, 33); info.MaximumSize = new Size(560, 0);
        var size = L(Fmt.Size(r.Bytes), 12, r.Bytes > 0 ? Theme.Violet : Theme.Muted, true, Theme.Surf); size.Location = new Point(400, 18);
        row.SizeChanged += (_, _) => size.Left = row.Width - size.Width - 22;
        row.Controls.AddRange(new Control[] { tg, name, info, size });
        return row;
    }

    // ---------- History ----------
    Control BuildHistory()
    {
        _historyBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Surf, ForeColor = Theme.Silver,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10f), Text = History.ReadAll()
        };
        Native.DarkScroll(_historyBox);
        var refresh = new GButton("Refresh");
        refresh.Click += (_, _) => { _historyBox.Text = History.ReadAll(); };
        var open = new GButton("Open log folder");
        open.Click += (_, _) => { try { Directory.CreateDirectory(Settings.DataDir); Process.Start("explorer.exe", Settings.DataDir); } catch { } };
        return Stack((Header("History", "Every cleanup is logged here. The log is small and rotates automatically."), false), (Bar(refresh, open), false), (_historyBox, true));
    }

    // ---------- Settings ----------
    Control BuildSettings()
    {
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
        Native.DarkScroll(flow);
        void Fit() { foreach (Control c in flow.Controls) c.Width = Math.Max(300, flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6); }
        flow.SizeChanged += (_, _) => Fit();

        Control Row(string title, string desc, Control right)
        {
            var card = new Card { Height = 68, Margin = new Padding(0, 0, 0, 10) };
            var t = L(title, 10.5f, Theme.Txt, true, Theme.Surf); t.Location = new Point(20, 12);
            var d = L(desc, 8.5f, Theme.Muted, false, Theme.Surf); d.Location = new Point(20, 36);
            right.Top = (card.Height - right.Height) / 2;
            card.SizeChanged += (_, _) => right.Left = card.Width - right.Width - 22;
            card.Controls.AddRange(new Control[] { t, d, right });
            return card;
        }
        Toggle Tg(bool v, Action<bool> set) { var t = new Toggle(); t.Checked = v; t.CheckedChanged += (_, _) => { set(t.Checked); _s.Save(); }; return t; }

        flow.Controls.Add(Row("Auto Clean on launch", "Scan safe TEMP locations when DREX starts and clean them automatically.", Tg(_s.AutoClean, v => _s.AutoClean = v)));
        flow.Controls.Add(Row("Confirm before Deep Clean", "Always ask before deleting anything outside the default safe TEMP locations.", Tg(_s.ConfirmDeepClean, v => _s.ConfirmDeepClean = v)));
        flow.Controls.Add(Row("RAM monitoring", "Refresh memory and disk figures every 5 seconds. Paused when minimized.", Tg(_s.RamMonitoring, v => { _s.RamMonitoring = v; StartRamTimer(); })));
        flow.Controls.Add(Row("Keep running in the system tray", "Closing or minimizing hides DREX to the tray instead of exiting. Use the tray menu to exit.", Tg(_s.CloseToTray, v => _s.CloseToTray = v)));
        flow.Controls.Add(Row("Launch with Windows", "Adds a per-user startup entry. No service or scheduled task is created.", Tg(IsStartupOn(), SetStartup)));
        var age = Num(0, 720, _s.MinAgeHours);
        age.ValueChanged += (_, _) => { _s.MinAgeHours = (int)age.Value; _s.Save(); };
        flow.Controls.Add(Row("Minimum file age (hours)", "Recently modified files are never touched by the safe cleaner.", age));
        if (!IsAdmin)
        {
            var adm = new GButton("Restart as admin");
            adm.Click += (_, _) => RestartAsAdmin();
            flow.Controls.Add(Row("Administrator mode", "Needed for Windows TEMP, the Website Blocker and Defender updates.", adm));
        }
        return Stack((Header("Settings", "Everything is saved locally in your user profile."), false), (flow, true));
    }

    void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath, "--elevated") { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static bool IsStartupOn() { try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("DREX_PC_CLEANER") != null; } catch { return false; } }
    static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true); if (k == null) return;
            if (on) k.SetValue("DREX_PC_CLEANER", $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\" --tray"); else k.DeleteValue("DREX_PC_CLEANER", false);
        }
        catch (Exception ex) { MessageBox.Show("Could not change startup setting: " + ex.Message); }
    }

    // ---------- RAM / disk (5 s tick, only while visible) ----------
    void StartRamTimer()
    {
        if (_s.RamMonitoring && Visible && WindowState != FormWindowState.Minimized) { UpdateSystem(); _ramTimer.Start(); }
        else { _ramTimer.Stop(); if (_ring != null) { _ring.Big = "—"; _ring.Small = "paused"; } }
    }

    void UpdateSystem()
    {
        if (_ring == null) return;
        var (total, avail, pct) = Native.Memory();
        if (total > 0)
        {
            _ring.Value = pct; _ring.Big = pct + "%"; _ring.Small = "used";
            _lblRamTxt.Text = $"{Fmt.Size((long)(total - avail))}\nof {Fmt.Size((long)total)}\n{Fmt.Size((long)avail)} free";
        }
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            long used = d.TotalSize - d.AvailableFreeSpace; int p = (int)(used * 100 / Math.Max(1, d.TotalSize));
            _lblDisk.Text = Fmt.Size(d.AvailableFreeSpace) + " free"; _diskBar.Value = p;
            _lblDiskSub.Text = $"{d.Name}  ·  {p}% of {Fmt.Size(d.TotalSize)} used";
        }
        catch { }
    }
}
