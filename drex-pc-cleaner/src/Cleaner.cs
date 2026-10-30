using System.Runtime.InteropServices;
namespace Drex;

public sealed record Category(string Name, string Path, bool Safe, bool Recycle = false);
public sealed class ScanResult { public long Bytes; public int Files; public int Errors; }
public sealed class CleanResult { public long Bytes; public int Deleted; public int Skipped; public int Errors; }

public static class Cleaner
{
    public static List<Category> SafeCategories()
    {
        var l = new List<Category> { new("User TEMP", Path.GetTempPath(), true) };
        var win = Environment.GetEnvironmentVariable("SystemRoot");
        if (win != null) l.Add(new("Windows TEMP", Path.Combine(win, "Temp"), true));
        return l;
    }

    public static List<Category> DeepCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        return new()
        {
            new("Thumbnail cache", Path.Combine(local, @"Microsoft\Windows\Explorer"), false),
            new("Crash dumps (user)", Path.Combine(local, "CrashDumps"), false),
            new("Windows Error Reporting", Path.Combine(local, @"Microsoft\Windows\WER"), false),
            new("Crash dumps (system)", Path.Combine(win, "Minidump"), false),
            new("Recycle Bin", "", false, true),
        };
    }

    // Thumbnail cache: only thumbcache_*.db files; everything else in Explorer folder is left alone.
    static bool Eligible(Category c, FileInfo f, TimeSpan minAge)
    {
        if (c.Name == "Thumbnail cache" && !(f.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))) return false;
        if ((f.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return DateTime.UtcNow - f.LastWriteTimeUtc >= minAge;
    }

    static IEnumerable<FileInfo> Enumerate(Category c, TimeSpan minAge, CancellationToken ct)
    {
        if (!Directory.Exists(c.Path)) yield break;
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // skip symlinks/junctions
            ReturnSpecialDirectories = false,
        };
        foreach (var f in new DirectoryInfo(c.Path).EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            if (Eligible(c, f, minAge)) yield return f;
        }
    }

    public static ScanResult Scan(Category c, TimeSpan minAge, CancellationToken ct)
    {
        var r = new ScanResult();
        if (c.Recycle) { r.Bytes = RecycleBin.Size(); return r; }
        try
        {
            foreach (var f in Enumerate(c, minAge, ct)) { try { r.Bytes += f.Length; r.Files++; } catch { r.Errors++; } }
        }
        catch (OperationCanceledException) { throw; }
        catch { r.Errors++; }
        return r;
    }

    public static CleanResult Clean(Category c, TimeSpan minAge, CancellationToken ct)
    {
        var r = new CleanResult();
        if (c.Recycle)
        {
            long before = RecycleBin.Size();
            if (RecycleBin.Empty()) { r.Bytes = before; r.Deleted = 1; } else r.Errors++;
            return r;
        }
        try
        {
            foreach (var f in Enumerate(c, minAge, ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long len = f.Length;
                    if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                    f.Delete();               // throws IOException if locked -> skipped
                    r.Bytes += len; r.Deleted++;
                }
                catch (IOException) { r.Skipped++; }
                catch (UnauthorizedAccessException) { r.Skipped++; }
                catch { r.Errors++; }
            }
            if (c.Safe) PruneEmptyDirs(c.Path, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { r.Errors++; }
        return r;
    }

    // Removes empty subfolders only (Directory.Delete without recurse fails on non-empty dirs).
    static void PruneEmptyDirs(string root, CancellationToken ct)
    {
        try
        {
            var dirs = new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .OrderByDescending(d => d.FullName.Length);
            foreach (var d in dirs)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - d.LastWriteTimeUtc < TimeSpan.FromHours(1)) continue;
                try { d.Delete(false); } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
    }
}

static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
    const uint NoConfirm = 1, NoProgress = 2, NoSound = 4;

    public static long Size()
    {
        var i = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref i) == 0 ? i.i64Size : 0;
    }
    public static bool Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirm | NoProgress | NoSound) == 0;
}

public static class Fmt
{
    public static string Size(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" }; double d = b; int i = 0;
        while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; }
        return i == 0 ? $"{b} B" : $"{d:0.##} {u[i]}";
    }
}
