using Microsoft.Win32;
using System.Diagnostics;
namespace Drex;

public enum StartupKind { HkcuRun, HklmRun, HklmRun32, UserFolder, CommonFolder }
public sealed record StartupEntry(string Name, string ValueName, string Command, string Location, string Publisher, string Note, StartupKind Kind, bool Enabled);

public static class StartupScanner
{
    static readonly string[] Suspicious = { "update", "updater", "helper", "launcher", "sync", "autoupdate" };
    const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static List<StartupEntry> Read()
    {
        var l = new List<StartupEntry>();
        Reg(l, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run", StartupKind.HkcuRun);
        Reg(l, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run", StartupKind.HklmRun);
        Reg(l, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run (32-bit)", StartupKind.HklmRun32);
        Folder(l, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup folder", StartupKind.UserFolder);
        Folder(l, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup folder", StartupKind.CommonFolder);
        return l;
    }

    static (RegistryKey hive, string sub) Approved(StartupKind k) => k switch
    {
        StartupKind.HkcuRun => (Registry.CurrentUser, ApprovedBase + @"\Run"),
        StartupKind.HklmRun => (Registry.LocalMachine, ApprovedBase + @"\Run"),
        StartupKind.HklmRun32 => (Registry.LocalMachine, ApprovedBase + @"\Run32"),
        StartupKind.UserFolder => (Registry.CurrentUser, ApprovedBase + @"\StartupFolder"),
        _ => (Registry.LocalMachine, ApprovedBase + @"\StartupFolder"),
    };

    static bool IsEnabled(StartupKind kind, string valueName)
    {
        try
        {
            var (h, s) = Approved(kind);
            using var k = h.OpenSubKey(s);
            if (k?.GetValue(valueName) is byte[] b && b.Length > 0) return (b[0] & 1) == 0;
        }
        catch { }
        return true;
    }

    // Reversible: only flips the same "StartupApproved" flag Windows Task Manager uses.
    public static (bool ok, string err) SetEnabled(StartupEntry e, bool enable)
    {
        try
        {
            var (h, s) = Approved(e.Kind);
            using var k = h.CreateSubKey(s, true);
            var data = new byte[12];
            data[0] = enable ? (byte)2 : (byte)3;
            k.SetValue(e.ValueName, data, RegistryValueKind.Binary);
            return (true, "");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    static void Reg(List<StartupEntry> l, RegistryKey hive, string path, string loc, StartupKind kind)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            if (k == null) return;
            foreach (var n in k.GetValueNames())
                l.Add(Make(n, n, k.GetValue(n)?.ToString() ?? "", loc, kind));
        }
        catch { }
    }

    static void Folder(List<StartupEntry> l, string dir, string loc, StartupKind kind)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                l.Add(Make(Path.GetFileNameWithoutExtension(f), Path.GetFileName(f), f, loc, kind));
            }
        }
        catch { }
    }

    static StartupEntry Make(string name, string valueName, string cmd, string loc, StartupKind kind)
    {
        string exe = ExtractExe(cmd), pub = "", note = "";
        try { if (File.Exists(exe)) pub = FileVersionInfo.GetVersionInfo(exe).CompanyName ?? ""; else note = "Target file not found"; } catch { }
        if (note == "" && Suspicious.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase) || exe.Contains(s, StringComparison.OrdinalIgnoreCase)))
            note = "Possibly unnecessary (updater/helper)";
        return new(name, valueName, cmd, loc, pub, note, kind, IsEnabled(kind, valueName));
    }

    static string ExtractExe(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith('"')) { int e = cmd.IndexOf('"', 1); return e > 0 ? cmd[1..e] : cmd.Trim('"'); }
        int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? cmd[..(i + 4)] : cmd;
    }

    public static void OpenSettings() { try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); } catch { } }
}
