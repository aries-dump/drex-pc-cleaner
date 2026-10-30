using Microsoft.Win32;
using System.Diagnostics;
namespace Drex;

public sealed record StartupEntry(string Name, string Command, string Location, string Publisher, string Note);

public static class StartupScanner
{
    static readonly string[] Suspicious = { "update", "updater", "helper", "launcher", "sync", "autoupdate" };

    public static List<StartupEntry> Read()
    {
        var l = new List<StartupEntry>();
        Reg(l, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run");
        Reg(l, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run");
        Reg(l, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run (32-bit)");
        Folder(l, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup folder");
        Folder(l, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup folder");
        return l;
    }

    static void Reg(List<StartupEntry> l, RegistryKey hive, string path, string loc)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            if (k == null) return;
            foreach (var n in k.GetValueNames())
            {
                var cmd = k.GetValue(n)?.ToString() ?? "";
                l.Add(Make(n, cmd, loc));
            }
        }
        catch { }
    }

    static void Folder(List<StartupEntry> l, string dir, string loc)
    {
        try { if (Directory.Exists(dir)) foreach (var f in Directory.EnumerateFiles(dir)) if (!f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)) l.Add(Make(Path.GetFileNameWithoutExtension(f), f, loc)); }
        catch { }
    }

    static StartupEntry Make(string name, string cmd, string loc)
    {
        string exe = ExtractExe(cmd), pub = "", note = "";
        try { if (File.Exists(exe)) pub = FileVersionInfo.GetVersionInfo(exe).CompanyName ?? ""; else note = "Target file not found"; } catch { }
        if (note == "" && Suspicious.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase) || exe.Contains(s, StringComparison.OrdinalIgnoreCase)))
            note = "Possibly unnecessary (updater/helper)";
        return new(name, cmd, loc, pub, note);
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
