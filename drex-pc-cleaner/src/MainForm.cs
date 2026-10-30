using System.Diagnostics;
using Microsoft.Win32;
namespace Drex;

public sealed class MainForm : Form
{
    static readonly Color Bg = ColorTranslator.FromHtml("#0B0D12"), Surf = ColorTranslator.FromHtml("#171B24"),
        Surf2 = ColorTranslator.FromHtml("#202531"), Txt = ColorTranslator.FromHtml("#F4F5F8"),
        Silver = ColorTranslator.FromHtml("#C4CAD5"), Muted = ColorTranslator.FromHtml("#8B93A4"),
        Violet = ColorTranslator.FromHtml("#9875FF");

    readonly Settings _s = Settings.Load();
    readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Bg };
    readonly Dictionary<string, Control> _pages = new();   // lazily created
    readonly Dictionary<string, Button> _nav = new();
    readonly System.Windows.Forms.Timer _ramTimer = new() { Interval = 5000 };
    CancellationTokenSource? _cts;
    bool _busy;

    // Dashboard controls
    Label _lblReclaim = null!, _lblStatus = null!, _lblRam = null!;
    ProgressBar _barRam = null!;
    Button _btnScan = null!, _btnClean = null!, _btnCancel = null!;
    ListView _lvCats = null!;
    List<Category> _lastCats = new();

    public MainForm()
    {
        Text = "DREX PC CLEANER"; BackColor = Bg; ForeColor = Txt; Font = new Font("Segoe UI", 9.5f);
        Size = new Size(960, 640); MinimumSize = new Size(820, 540); StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        DoubleBuffered = true;

        var side = new Panel { Dock = DockStyle.Left, Width = 190, BackColor = Surf };
        var brand = new Label { Text = "DREX", Font = new Font("Segoe UI Semibold", 20f), ForeColor = Violet, Dock = DockStyle.Top, Height = 52, Padding = new Padding(16, 10, 0, 0) };
        var sub = new Label { Text = "PC CLEANER", ForeColor = Silver, Dock = DockStyle.Top, Height = 26, Padding = new Padding(18, 0, 0, 0) };
        side.Controls.Add(sub); side.Controls.Add(brand);
        var navHost = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 16, 0, 0) };
        side.Controls.Add(navHost); navHost.BringToFront();
        foreach (var n in new[] { "Dashboard", "Deep Clean", "Startup", "Large Files", "History", "Settings" })
        {
            var b = NavButton(n); _nav[n] = b; navHost.Controls.Add(b);
        }
        Controls.Add(_content); Controls.Add(side);

        _ramTimer.Tick += (_, _) => UpdateRam();
        Shown += async (_, _) => { Show("Dashboard"); StartRamTimer(); await RunLaunchFlow(); };
        FormClosing += (_, _) => { _cts?.Cancel(); _ramTimer.Stop(); _s.Save(); };
        // Stop polling when minimized
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) _ramTimer.Stop(); else StartRamTimer(); };
    }

    Button NavButton(string name)
    {
        var b = new Button { Text = "  " + name, Width = 190, Height = 40, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Silver, BackColor = Surf, Margin = new Padding(0), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Surf2;
        b.Click += (_, _) => Show(name);
        return b;
    }

    void Show(string name)
    {
        foreach (var kv in _nav) { kv.Value.ForeColor = kv.Key == name ? Violet : Silver; kv.Value.BackColor = kv.Key == name ? Surf2 : Surf; }
        if (!_pages.TryGetValue(name, out var page))
        {
            page = name switch { "Dashboard" => BuildDashboard(), "Deep Clean" => BuildDeep(), "Startup" => BuildStartup(), "Large Files" => BuildLarge(), "History" => BuildHistory(), _ => BuildSettings() };
            page.Dock = DockStyle.Fill; _pages[name] = page; _content.Controls.Add(page);
        }
        page.BringToFront();
        if (name == "History") ((TextBox)page.Controls.OfType<TextBox>().First()).Text = History.ReadAll();
    }

    // ---------- helpers ----------
    Label Lbl(string t, float size = 9.5f, Color? c = null, bool bold = false) =>
        new() { Text = t, AutoSize = true, ForeColor = c ?? Txt, Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size) };

    Button Btn(string t, bool primary = false)
    {
        var b = new Button { Text = t, AutoSize = true, MinimumSize = new Size(110, 34), FlatStyle = FlatStyle.Flat, BackColor = primary ? Violet : Surf2, ForeColor = primary ? Color.White : Txt, Cursor = Cursors.Hand, Padding = new Padding(8, 0, 8, 0) };
        b.FlatAppearance.BorderColor = primary ? Violet : Surf2; b.FlatAppearance.MouseOverBackColor = primary ? ColorTranslator.FromHtml("#AE92FF") : ColorTranslator.FromHtml("#2A3040");
        return b;
    }

    ListView LV(params (string, int)[] cols)
    {
        var lv = new ListView { View = View.Details, FullRowSelect = true, BackColor = Surf, ForeColor = Txt, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, HideSelection = false, GridLines = false };
        foreach (var (n, w) in cols) lv.Columns.Add(n, w);
        return lv;
    }

    Panel Page(string title, out FlowLayoutPanel bar)
    {
        var p = new Panel { Padding = new Padding(24), BackColor = Bg };
        var h = Lbl(title, 18, Txt, true); h.Dock = DockStyle.Top; h.AutoSize = false; h.Height = 40;
        bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false, BackColor = Bg };
        p.Controls.Add(bar); p.Controls.Add(h);
        return p;
    }

    void SetBusy(bool b)
    {
        _busy = b;
        if (_btnScan != null) { _btnScan.Enabled = !b; _btnClean.Enabled = !b; _btnCancel.Enabled = b; }
    }

    TimeSpan MinAge => TimeSpan.FromHours(Math.Max(0, _s.MinAgeHours));

    // ---------- Dashboard ----------
    Control BuildDashboard()
    {
        var p = Page("Dashboard", out var bar);
        _btnScan = Btn("Scan TEMP"); _btnClean = Btn("Clean safe files", true); _btnCancel = Btn("Cancel"); _btnCancel.Enabled = false;
        _btnScan.Click += async (_, _) => await ScanSafe();
        _btnClean.Click += async (_, _) => await CleanSafe();
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        bar.Controls.AddRange(new Control[] { _btnScan, _btnClean, _btnCancel });

        _lblReclaim = Lbl("—", 30, Violet, true); _lblStatus = Lbl("Starting…", 10, Muted);
        _lvCats = LV(("Location", 160), ("Files", 80), ("Size", 100), ("Path", 420));
        _lblRam = Lbl("RAM: —", 10, Silver); _barRam = new ProgressBar { Width = 400, Height = 10, Style = ProgressBarStyle.Continuous };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 120, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(Lbl("Reclaimable (safe TEMP)", 10, Muted)); top.Controls.Add(_lblReclaim); top.Controls.Add(_lblStatus);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        bottom.Controls.Add(_lblRam); bottom.Controls.Add(_barRam);
        p.Controls.Add(_lvCats); p.Controls.Add(bottom); p.Controls.Add(top); _lvCats.BringToFront();
        return p;
    }

    async Task RunLaunchFlow()
    {
        await ScanSafe();
        if (_s.AutoClean && _lastCats.Count > 0) await CleanSafe(auto: true);
    }

    async Task ScanSafe()
    {
        if (_busy) return; SetBusy(true); _cts = new(); var ct = _cts.Token; var age = MinAge;
        _lblStatus.Text = "Scanning…"; _lvCats.Items.Clear();
        try
        {
            var cats = Cleaner.SafeCategories(); long total = 0;
            foreach (var c in cats)
            {
                var r = await Task.Run(() => Cleaner.Scan(c, age, ct), ct);
                total += r.Bytes;
                _lvCats.Items.Add(new ListViewItem(new[] { c.Name, r.Files.ToString(), Fmt.Size(r.Bytes), c.Path }));
            }
            _lastCats = cats; _lblReclaim.Text = Fmt.Size(total);
            _lblStatus.Text = $"Scan complete. Files older than {_s.MinAgeHours} h only. Locked files are skipped at clean time.";
        }
        catch (OperationCanceledException) { _lblStatus.Text = "Scan cancelled."; }
        catch (Exception ex) { _lblStatus.Text = "Scan error: " + ex.Message; }
        finally { SetBusy(false); }
    }

    async Task CleanSafe(bool auto = false)
    {
        if (_busy) return; SetBusy(true); _cts = new(); var ct = _cts.Token; var age = MinAge;
        _lblStatus.Text = auto ? "Auto Clean running…" : "Cleaning…";
        try
        {
            var total = new CleanResult();
            foreach (var c in _lastCats)
            {
                var r = await Task.Run(() => Cleaner.Clean(c, age, ct), ct);
                total.Bytes += r.Bytes; total.Deleted += r.Deleted; total.Skipped += r.Skipped; total.Errors += r.Errors;
            }
            var msg = $"{(auto ? "Auto" : "Manual")} clean: freed {Fmt.Size(total.Bytes)}, {total.Deleted} deleted, {total.Skipped} skipped (locked/in use), {total.Errors} errors";
            History.Add(msg); _lblStatus.Text = msg; _lblReclaim.Text = "0 B";
        }
        catch (OperationCanceledException) { _lblStatus.Text = "Cleanup cancelled (partial results are in History)."; History.Add("Cleanup cancelled"); }
        catch (Exception ex) { _lblStatus.Text = "Cleanup error: " + ex.Message; History.Add("Cleanup error: " + ex.Message); }
        finally { SetBusy(false); }
        if (!_busy) { _busy = false; await ScanSafeQuiet(); }
    }

    async Task ScanSafeQuiet()
    {
        // Refresh numbers after cleaning, preserving the cleanup report text.
        var keep = _lblStatus.Text; await ScanSafe(); _lblStatus.Text = keep;
    }

    // ---------- Deep Clean ----------
    Control BuildDeep()
    {
        var p = Page("Deep Clean", out var bar);
        var lv = LV(("Category", 200), ("Files", 70), ("Size", 100), ("Path", 420)); lv.CheckBoxes = true;
        var scan = Btn("Analyze"); var clean = Btn("Clean selected", true); var cancel = Btn("Cancel"); cancel.Enabled = false;
        var note = Lbl("Nothing here is deleted without your confirmation. Files in use are skipped.", 9, Muted); note.Dock = DockStyle.Bottom;
        bar.Controls.AddRange(new Control[] { scan, clean, cancel });
        var cats = Cleaner.DeepCategories(); var map = new Dictionary<ListViewItem, Category>();

        scan.Click += async (_, _) =>
        {
            if (_busy) return; SetBusy(true); cancel.Enabled = true; _cts = new(); var ct = _cts.Token; lv.Items.Clear(); map.Clear();
            try
            {
                foreach (var c in cats)
                {
                    var r = await Task.Run(() => Cleaner.Scan(c, TimeSpan.FromHours(1), ct), ct);
                    var it = new ListViewItem(new[] { c.Name, c.Recycle ? "—" : r.Files.ToString(), Fmt.Size(r.Bytes), c.Recycle ? "(shell)" : c.Path });
                    lv.Items.Add(it); map[it] = c;
                }
            }
            catch (OperationCanceledException) { } catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { SetBusy(false); cancel.Enabled = false; }
        };
        clean.Click += async (_, _) =>
        {
            if (_busy) return;
            var sel = lv.CheckedItems.Cast<ListViewItem>().ToList();
            if (sel.Count == 0) { MessageBox.Show("Select at least one category."); return; }
            if (_s.ConfirmDeepClean && MessageBox.Show("Permanently delete the selected categories?\n\n" + string.Join("\n", sel.Select(i => $"• {i.Text} ({i.SubItems[2].Text})")),
                "Confirm Deep Clean", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SetBusy(true); cancel.Enabled = true; _cts = new(); var ct = _cts.Token; long freed = 0;
            try
            {
                foreach (var it in sel) { var c = map[it]; var r = await Task.Run(() => Cleaner.Clean(c, TimeSpan.FromHours(1), ct), ct); freed += r.Bytes;
                    History.Add($"Deep clean [{c.Name}]: freed {Fmt.Size(r.Bytes)}, {r.Deleted} deleted, {r.Skipped} skipped, {r.Errors} errors"); }
                MessageBox.Show($"Deep Clean finished. Freed {Fmt.Size(freed)}.");
            }
            catch (OperationCanceledException) { History.Add("Deep clean cancelled"); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { SetBusy(false); cancel.Enabled = false; }
        };
        cancel.Click += (_, _) => _cts?.Cancel();
        p.Controls.Add(lv); p.Controls.Add(note); lv.BringToFront();
        return p;
    }

    // ---------- Startup ----------
    Control BuildStartup()
    {
        var p = Page("Startup", out var bar);
        var lv = LV(("Name", 180), ("Publisher", 160), ("Location", 140), ("Note", 190), ("Command", 400));
        var refresh = Btn("Refresh"); var open = Btn("Open Startup Apps settings", true);
        bar.Controls.AddRange(new Control[] { refresh, open });
        void Load()
        {
            lv.Items.Clear();
            foreach (var e in StartupScanner.Read()) { var it = new ListViewItem(new[] { e.Name, e.Publisher, e.Location, e.Note, e.Command }); if (e.Note != "") it.ForeColor = Violet; lv.Items.Add(it); }
        }
        refresh.Click += (_, _) => Load(); open.Click += (_, _) => StartupScanner.OpenSettings();
        Load();
        p.Controls.Add(lv); lv.BringToFront();
        return p;
    }

    // ---------- Large Files ----------
    Control BuildLarge()
    {
        var p = Page("Large Files", out var bar);
        var path = new TextBox { Width = 260, BackColor = Surf2, ForeColor = Txt, BorderStyle = BorderStyle.FixedSingle, Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Margin = new Padding(3, 7, 3, 3) };
        var browse = Btn("Browse…");
        var mb = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = Math.Max(1, _s.LargeFileMb), Width = 80, BackColor = Surf2, ForeColor = Txt, Margin = new Padding(3, 7, 3, 3) };
        var scan = Btn("Scan", true); var cancel = Btn("Cancel"); cancel.Enabled = false; var show = Btn("Open location");
        bar.Controls.AddRange(new Control[] { path, browse, Lbl("  Min MB:", 9.5f, Muted), mb, scan, cancel, show });
        var lv = LV(("Name", 220), ("Size", 100), ("Path", 520)); var status = Lbl("", 9, Muted); status.Dock = DockStyle.Bottom;
        var items = new List<BigFile>();

        browse.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = path.Text }; if (d.ShowDialog() == DialogResult.OK) path.Text = d.SelectedPath; };
        scan.Click += async (_, _) =>
        {
            if (!Directory.Exists(path.Text)) { MessageBox.Show("Folder not found."); return; }
            if (_busy) return; SetBusy(true); scan.Enabled = false; cancel.Enabled = true; _cts = new(); var ct = _cts.Token;
            _s.LargeFileMb = (int)mb.Value; long min = (long)mb.Value * 1024 * 1024; items.Clear(); lv.Items.Clear(); status.Text = "Scanning…";
            var sync = SynchronizationContext.Current!; var pending = new System.Collections.Concurrent.ConcurrentQueue<BigFile>();
            using var tick = new System.Windows.Forms.Timer { Interval = 500 };
            tick.Tick += (_, _) => Drain();
            void Drain() { while (pending.TryDequeue(out var f)) items.Add(f); }
            tick.Start();
            try { await Task.Run(() => LargeFileFinder.Scan(path.Text, min, f => pending.Enqueue(f), ct), ct); status.Text = "Scan complete."; }
            catch (OperationCanceledException) { status.Text = "Scan cancelled."; }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
            finally { tick.Stop(); Drain(); SetBusy(false); scan.Enabled = true; cancel.Enabled = false; }
            lv.BeginUpdate();
            foreach (var f in items.OrderByDescending(x => x.Size)) lv.Items.Add(new ListViewItem(new[] { f.Name, Fmt.Size(f.Size), f.Path }));
            lv.EndUpdate(); status.Text += $" {items.Count} file(s) found.";
        };
        cancel.Click += (_, _) => _cts?.Cancel();
        show.Click += (_, _) => { if (lv.SelectedItems.Count > 0) try { Process.Start("explorer.exe", $"/select,\"{lv.SelectedItems[0].SubItems[2].Text}\""); } catch { } };
        p.Controls.Add(lv); p.Controls.Add(status); lv.BringToFront();
        return p;
    }

    // ---------- History ----------
    Control BuildHistory()
    {
        var p = Page("History", out _);
        var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Surf, ForeColor = Silver, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f) };
        p.Controls.Add(tb); tb.BringToFront();
        return p;
    }

    // ---------- Settings ----------
    Control BuildSettings()
    {
        var p = Page("Settings", out _);
        var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        CheckBox Chk(string t, bool v, Action<bool> set) { var c = new CheckBox { Text = t, Checked = v, AutoSize = true, ForeColor = Txt, Margin = new Padding(3, 8, 3, 8) }; c.CheckedChanged += (_, _) => { set(c.Checked); _s.Save(); }; return c; }
        f.Controls.Add(Chk("Auto Clean on launch", _s.AutoClean, v => _s.AutoClean = v));
        f.Controls.Add(Chk("Require confirmation before Deep Clean", _s.ConfirmDeepClean, v => _s.ConfirmDeepClean = v));
        f.Controls.Add(Chk("RAM monitoring (every 5 s)", _s.RamMonitoring, v => { _s.RamMonitoring = v; StartRamTimer(); }));
        f.Controls.Add(Chk("Launch with Windows (current user)", IsStartupOn(), SetStartup));
        var age = new NumericUpDown { Minimum = 0, Maximum = 720, Value = Math.Clamp(_s.MinAgeHours, 0, 720), Width = 80, BackColor = Surf2, ForeColor = Txt };
        age.ValueChanged += (_, _) => { _s.MinAgeHours = (int)age.Value; _s.Save(); };
        var row = new FlowLayoutPanel { AutoSize = true }; row.Controls.Add(Lbl("Minimum file age (hours): ", 9.5f, Silver)); row.Controls.Add(age);
        f.Controls.Add(row);
        f.Controls.Add(Lbl("Large-file threshold is set on the Large Files page and remembered.", 9, Muted));
        p.Controls.Add(f); f.BringToFront();
        return p;
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static bool IsStartupOn() { try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("DREX_PC_CLEANER") != null; } catch { return false; } }
    static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true); if (k == null) return;
            if (on) k.SetValue("DREX_PC_CLEANER", $"\"{Application.ExecutablePath}\""); else k.DeleteValue("DREX_PC_CLEANER", false);
        }
        catch (Exception ex) { MessageBox.Show("Could not change startup setting: " + ex.Message); }
    }

    // ---------- RAM ----------
    void StartRamTimer()
    {
        if (_s.RamMonitoring && WindowState != FormWindowState.Minimized) { UpdateRam(); _ramTimer.Start(); }
        else { _ramTimer.Stop(); if (_lblRam != null) _lblRam.Text = "RAM monitoring is off"; }
    }

    void UpdateRam()
    {
        if (_lblRam == null) return;
        var (total, avail, pct) = Native.Memory();
        if (total == 0) { _lblRam.Text = "RAM: unavailable"; return; }
        _lblRam.Text = $"RAM  total {Fmt.Size((long)total)}   used {Fmt.Size((long)(total - avail))}   available {Fmt.Size((long)avail)}   ({pct}%)";
        _barRam.Value = (int)Math.Min(100, pct);
    }
}
