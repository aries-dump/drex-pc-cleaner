using System.Text.Json;
namespace Drex;

public sealed class Settings
{
    public bool AutoClean { get; set; } = true;
    public int MinAgeHours { get; set; } = 1;
    public bool ConfirmDeepClean { get; set; } = true;
    public bool RamMonitoring { get; set; } = true;
    public int LargeFileMb { get; set; } = 500;
    public bool CloseToTray { get; set; } = true;
    public bool CloseAsked { get; set; } = false;
    public bool UsbAutoScan { get; set; } = false;
    public bool FirstLaunchCompleted { get; set; } = false;
    public bool AutoLoginEnabled { get; set; } = true;

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DrexPcCleaner");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    public static string DataDir => Dir;

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); } catch { }
        return new();
    }
    public void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }
}

// Small append-only history log, rotated at 128 KB (keeps one .old file).
public static class History
{
    public static void Add(string line)
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            var p = Path.Combine(Settings.DataDir, "history.log");
            var fi = new FileInfo(p);
            if (fi.Exists && fi.Length > 128 * 1024) File.Move(p, p + ".old", true);
            File.AppendAllText(p, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch { }
    }
    public static string ReadAll()
    {
        try { var p = Path.Combine(Settings.DataDir, "history.log"); return File.Exists(p) ? File.ReadAllText(p) : "No cleanup history yet."; }
        catch { return "Unable to read history."; }
    }
}
