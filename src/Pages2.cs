using System.Diagnostics;
namespace Drex;

public sealed partial class MainForm : Form
{
    static readonly string[] Days = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
    static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
    static string FirstLine(string s) => (s ?? "").Replace("\r", "").Split('\n')[0].Trim();

    // ---------- Scheduler ----------
    Control BuildScheduler()
    {
        var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
        Native.DarkScroll(host);
        void Fit() { foreach (Control c in host.Controls) c.Width = Math.Max(480, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6); }
        host.SizeChanged += (_, _) => Fit();
        host.Controls.Add(ScheduleCard("Weekly quick virus scan", "Runs a Windows Defender quick scan in the background. Creating it needs administrator rights.", "DREX Weekly Quick Scan", 0, false));
        host.Controls.Add(ScheduleCard("Weekly junk cleanup", "Cleans the safe TEMP locations (same rules as Auto Clean) without opening a window.", "DREX Weekly Cleanup", 1, true));
        return Stack((Header("Scheduler", "Optional tasks using Windows Task Scheduler. Nothing is scheduled unless you enable it."), false), (host, true));
    }

    Control ScheduleCard(string title, string desc, string taskName, int dayIndex, bool isClean)
    {
        var card = new Card { Height = 160, Margin = new Padding(0, 0, 0, 12) };
        var t = L(title, 10.5f, Theme.Txt, true, Theme.Surf); t.Location = new Point(20, 12);
        var d = L(desc, 8.5f, Theme.Muted, false, Theme.Surf); d.Location = new Point(20, 36);
        var day = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surf2, ForeColor = Theme.Txt, Width = 120, Location = new Point(20, 80), Font = Theme.F(10f) };
        day.Items.AddRange(DayNames); day.SelectedIndex = dayIndex;
        var at = L("at", 9.5f, Theme.Muted, false, Theme.Surf); at.Location = new Point(150, 84);
        NumericUpDown N(int max, int val, int x) => new() { Minimum = 0, Maximum = max, Value = val, Width = 52, Location = new Point(x, 80), BackColor = Theme.Surf2, ForeColor = Theme.Txt, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(10f) };
        var hh = N(23, 13, 172); var colon = L(":", 11, Theme.Muted, true, Theme.Surf); colon.Location = new Point(228, 82); var mm = N(59, 0, 242);
        var enable = new GButton("Enable / update", true) { Location = new Point(316, 70) };
        var remove = new GButton("Remove") { Location = new Point(466, 70) };
        var status = L("Checking…", 9f, Theme.Silver, false, Theme.Surf); status.Location = new Point(20, 124);

        async Task Refresh() { var s = await Task.Run(() => TaskSched.Status(taskName)); status.Text = s; }
        enable.Click += async (_, _) =>
        {
            enable.Enabled = false; status.Text = "Working…";
            string dd = Days[day.SelectedIndex], tm = $"{(int)hh.Value:00}:{(int)mm.Value:00}";
            string cmd = isClean ? $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\" --autoclean" : "powershell.exe -NoProfile -WindowStyle Hidden -Command \"Start-MpScan -ScanType QuickScan\"";
            var r = await Task.Run(() => TaskSched.CreateWeekly(taskName, dd, tm, cmd, !isClean));
            History.Add($"Scheduler: {(r.ok ? "created" : "failed")} '{taskName}' ({dd} {tm})" + (r.ok ? "" : " - " + FirstLine(r.msg)));
            await Refresh();
            if (!r.ok) status.Text = "Unable to create scheduled task. " + (IsAdmin || isClean ? "Check task name and try again." : "Use “Restart as admin” in Settings first.");
            enable.Enabled = true;
        };
        remove.Click += async (_, _) =>
        {
            remove.Enabled = false;
            var r = await Task.Run(() => TaskSched.Delete(taskName));
            History.Add($"Scheduler: removed '{taskName}'");
            await Refresh();
            remove.Enabled = true;
        };
        card.Controls.AddRange(new Control[] { t, d, day, at, hh, colon, mm, enable, remove, status });
        card.HandleCreated += async (_, _) => await Refresh();
        return card;
    }

    // ---------- Event log (Windows Defender operational log) ----------
    Control BuildEvents()
    {
        var refresh = new GButton("Refresh", true); var open = new GButton("Windows Security");
        var list = new DarkList(("Time", 160), ("ID", 70), ("Level", 110), ("Message", 760));
        var status = StatusLabel("Windows Defender operational log (latest 150 events).");
        async Task Load()
        {
            refresh.Enabled = false; status.Text = "Reading event log…";
            try
            {
                var rows = await Task.Run(() => Security.Events());
                list.BeginUpdate(); list.Items.Clear();
                foreach (var e in rows)
                {
                    var it = new ListViewItem(new[] { e.Time, e.Id.ToString(), e.Level, e.Message }) { };
                    it.UseItemStyleForSubItems = false;
                    if (e.Level.Contains("Error", StringComparison.OrdinalIgnoreCase)) it.SubItems[2].ForeColor = Theme.Bad;
                    else if (e.Level.Contains("Warning", StringComparison.OrdinalIgnoreCase)) it.SubItems[2].ForeColor = Theme.Warn;
                    list.Items.Add(it);
                }
                list.EndUpdate();
                status.Text = $"{rows.Count} event(s).";
            }
            catch (Exception ex) { status.Text = "Unable to read event log. " + (IsAdmin ? "Check Windows Event Log service." : "Use “Restart as admin” in Settings."); }
            finally { refresh.Enabled = true; }
        }
        refresh.Click += async (_, _) => await Load();
        open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); } catch { } };
        var page = Stack((Header("Event Log", "Security events recorded by Windows Defender."), false), (Bar(refresh, open), false), (list, true), (status, false));
        page.HandleCreated += async (_, _) => await Load();
        return page;
    }

    // ---------- File Vault ----------
    Control BuildVault()
    {
        var path = Input("", 380, "Choose a file…"); var browse = new GButton("Browse…");
        var pw = Input("", 230, "Password (8+ characters)"); pw.UseSystemPasswordChar = true;
        var pw2 = Input("", 230, "Repeat password (to encrypt)"); pw2.UseSystemPasswordChar = true;
        var enc = new GButton("Encrypt", true); var dec = new GButton("Decrypt"); var shred = new GButton("Secure delete…"); var cancel = new GButton("Cancel") { Enabled = false };
        var recycle = new Toggle { Margin = new Padding(0, 8, 8, 0) };
        var pb = new DarkBar(); var status = StatusLabel("AES-256-GCM encryption. Your original file is kept unless you choose otherwise.");
        var note = L("If you forget the password the file cannot be recovered. Secure delete overwrites once and cannot be guaranteed on SSDs, cloud-synced folders or backups.", 8.5f, Theme.Muted);
        note.MaximumSize = new Size(900, 0);

        void Busy(bool b) { enc.Enabled = !b; dec.Enabled = !b; shred.Enabled = !b; cancel.Enabled = b; }
        browse.Click += (_, _) => { using var d = new OpenFileDialog(); if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.FileName; };
        cancel.Click += (_, _) => _cts?.Cancel();

        async Task Work(string verb, Action<Action<int>, CancellationToken> job, Func<string> done, string? cleanupOnFail)
        {
            if (!Begin(out var ct)) return;
            Busy(true); pb.Value = 0; status.Text = verb + "…";
            var prog = new Progress<int>(v => pb.Value = v);
            try
            {
                await Task.Run(() => job(v => ((IProgress<int>)prog).Report(v), ct), ct);
                pb.Value = 100; status.Text = done();
            }
            catch (OperationCanceledException) { status.Text = verb + " cancelled."; if (cleanupOnFail != null) try { File.Delete(cleanupOnFail); } catch { } }
            catch (Exception ex) { status.Text = verb + " failed: " + FirstLine(ex.Message); if (cleanupOnFail != null) try { File.Delete(cleanupOnFail); } catch { } }
            End(); Busy(false);
        }

        enc.Click += async (_, _) =>
        {
            string src = path.Text;
            if (!File.Exists(src)) { MessageBox.Show(this, "Choose a file first.", "DREX PC CLEANER"); return; }
            if (pw.Text.Length < 8) { MessageBox.Show(this, "Use a password of at least 8 characters.", "DREX PC CLEANER"); return; }
            if (pw.Text != pw2.Text) { MessageBox.Show(this, "The two passwords do not match.", "DREX PC CLEANER"); return; }
            string dest = src + ".drex", pass = pw.Text; bool rec = recycle.Checked;
            if (File.Exists(dest)) { MessageBox.Show(this, "An encrypted copy already exists next to the file:\n" + dest, "DREX PC CLEANER"); return; }
            await Work("Encrypting", (p, ct) => Vault.Encrypt(src, dest, pass, p, ct),
                () => { if (rec && Native.Recycle(src)) { History.Add("Vault: encrypted " + src + " (original moved to Recycle Bin)"); return "Encrypted → " + dest + "  (original moved to the Recycle Bin)"; } History.Add("Vault: encrypted " + src); return "Encrypted → " + dest; }, dest);
        };
        dec.Click += async (_, _) =>
        {
            string src = path.Text;
            if (!File.Exists(src)) { MessageBox.Show(this, "Choose a .drex file first.", "DREX PC CLEANER"); return; }
            string dest = src.EndsWith(".drex", StringComparison.OrdinalIgnoreCase) ? src[..^5] : src + ".decrypted";
            if (File.Exists(dest)) dest = Path.Combine(Path.GetDirectoryName(dest) ?? "", Path.GetFileNameWithoutExtension(dest) + " (decrypted)" + Path.GetExtension(dest));
            if (File.Exists(dest)) { MessageBox.Show(this, "The output file already exists:\n" + dest, "DREX PC CLEANER"); return; }
            string pass = pw.Text, outp = dest;
            await Work("Decrypting", (p, ct) => Vault.Decrypt(src, outp, pass, p, ct), () => { History.Add("Vault: decrypted " + src); return "Decrypted → " + outp; }, outp);
        };
        shred.Click += async (_, _) =>
        {
            string src = path.Text;
            if (!File.Exists(src)) { MessageBox.Show(this, "Choose a file first.", "DREX PC CLEANER"); return; }
            if (MessageBox.Show(this, "PERMANENTLY destroy this file? It will NOT go to the Recycle Bin and cannot be recovered.\n\n" + src, "Secure delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await Work("Secure delete", (p, ct) => Vault.Shred(src, p, ct), () => { History.Add("Vault: shredded " + src); return "File destroyed: " + src; }, null);
        };

        var recycleRow = Bar(recycle, L("Move the original to the Recycle Bin after encrypting", 9.5f, Theme.Silver));
        foreach (Control c in recycleRow.Controls) if (c is Label) c.Margin = new Padding(0, 12, 0, 0);
        return Stack((Header("File Vault", "Encrypt and decrypt files with a password, or securely destroy a file."), false),
            (Bar(path, browse), false), (Bar(pw, pw2), false), (Bar(enc, dec, shred, cancel), false), (recycleRow, false), (pb, false), (status, false), (note, false));
    }
}
