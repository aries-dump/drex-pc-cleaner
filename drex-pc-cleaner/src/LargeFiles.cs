namespace Drex;

public sealed record BigFile(string Name, long Size, string Path);

public static class LargeFileFinder
{
    // Streams results through a callback; keeps only matches (bounded by user threshold) in memory.
    public static void Scan(string root, long minBytes, Action<BigFile> found, CancellationToken ct)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try { if (f.Length >= minBytes) found(new(f.Name, f.Length, f.FullName)); } catch { }
        }
    }
}
