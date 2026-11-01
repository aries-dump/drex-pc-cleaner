using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Drex;

public sealed record Detection(string Path, string Name, string Sha256, long Size);
public sealed record QuarItem(string Id, string OriginalPath, string Name, string Sha256, DateTime When);
public sealed class ScanReport { public List<Detection> Found = new(); public int Scanned, SkippedLarge, Errors; }

// Compact hash storage: sorted ulong[] array (2 ulongs per hash) with binary search.
// Much more memory-efficient than HashSet<(ulong, ulong)> - roughly 16 bytes per entry vs ~40-60.
public sealed class HashSetCompact
{
    readonly ulong[] _data; // pairs: _data[2*i] = hi, _data[2*i+1] = lo
    public int Count => _data.Length / 2;

    public HashSetCompact(ulong[] data) => _data = data;

    public bool Contains((ulong hi, ulong lo) key)
    {
        int left = 0, right = Count - 1;
        while (left <= right)
        {
            int mid = (left + right) >> 1;
            ulong midHi = _data[mid * 2];
            int cmp = midHi.CompareTo(key.hi);
            if (cmp == 0)
            {
                cmp = _data[mid * 2 + 1].CompareTo(key.lo);
            }
            if (cmp == 0) return true;
            if (cmp < 0) left = mid + 1;
            else right = mid - 1;
        }
        return false;
    }

    public static HashSetCompact Create(IEnumerable<(ulong hi, ulong lo)> keys)
    {
        var list = keys.OrderBy(k => k.hi).ThenBy(k => k.lo).ToList();
        var data = new ulong[list.Count * 2];
        for (int i = 0; i < list.Count; i++)
        {
            data[i * 2] = list[i].hi;
            data[i * 2 + 1] = list[i].lo;
        }
        return new HashSetCompact(data);
    }

    public IEnumerator<(ulong hi, ulong lo)> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return (_data[i * 2], _data[i * 2 + 1]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// DREX's own light scanner. It is NOT a full antivirus: it only detects (1) the standard EICAR test file and
// (2) files whose SHA-256 appears in a hash list that YOU import (e.g. a public malware-hash export).
// Windows Defender remains the real protection.
public static class HashScanner
{
    static string DataFile => Path.Combine(Settings.DataDir, "hashes.txt");
    const long MaxFile = 256L * 1024 * 1024;
    const int MaxHashes = 1_000_000;

    // The EICAR test string is built from pieces so DREX itself is never mistaken for a test virus.
    static string EicarText => "X5O!P%@AP[4\\PZX54(P^)7CC)7}$" + "EICAR-STANDARD-" + "ANTIVIRUS-TEST-FILE!" + "$H+H*";

    static bool TryKey(string token, out (ulong hi, ulong lo) key)
    {
        key = default;
        if (token.Length != 64) return false;
        foreach (char c in token) if (!Uri.IsHexDigit(c)) return false;
        key = (Convert.ToUInt64(token[..16], 16), Convert.ToUInt64(token[16..32], 16));
        return true;
    }

    public static HashSetCompact Load()
    {
        var list = new List<(ulong hi, ulong lo)>();
        try
        {
            if (!File.Exists(DataFile)) return HashSetCompact.Create(list);
            foreach (var line in File.ReadLines(DataFile))
            {
                var t = line.Trim(); if (t.Length == 0 || t[0] == '#') continue;
                var tok = t.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)[0];
                if (TryKey(tok, out var k)) list.Add(k);
                if (list.Count >= MaxHashes) break;
            }
        }
        catch { }
        return HashSetCompact.Create(list);
    }

    // Appends only valid 64-hex SHA-256 values from the chosen text file.
    public static int Import(string file)
    {
        // Load existing keys from file directly
        var existingKeys = new List<(ulong hi, ulong lo)>();
        try
        {
            if (File.Exists(DataFile))
            {
                foreach (var line in File.ReadLines(DataFile))
                {
                    var t = line.Trim(); if (t.Length == 0 || t[0] == '#') continue;
                    var tok = t.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (TryKey(tok, out var k)) existingKeys.Add(k);
                }
            }
        }
        catch { }

        Directory.CreateDirectory(Settings.DataDir);
        using var w = new StreamWriter(DataFile, true);
        int added = 0;
        var seen = new HashSet<(ulong hi, ulong lo)>(existingKeys);
        foreach (var line in File.ReadLines(file))
        {
            var t = line.Trim(); if (t.Length == 0 || t[0] == '#') continue;
            var tok = t.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (TryKey(tok, out var k) && seen.Count < MaxHashes && seen.Add(k))
            {
                w.WriteLine(tok.ToLowerInvariant());
                added++;
            }
        }
        return added;
    }

    static bool IsEicar(byte[] buf, int n) => n > 0 && n <= 128 && Encoding.ASCII.GetString(buf, 0, n).Contains(EicarText, StringComparison.Ordinal);

    // Runs the detection logic on in-memory samples (nothing is written to disk, so no antivirus is triggered).
    public static string SelfTest()
    {
        var e = Encoding.ASCII.GetBytes(EicarText);
        bool a = IsEicar(e, e.Length);
        var clean = Encoding.ASCII.GetBytes("hello world");
        bool b = !IsEicar(clean, clean.Length);
        var h = SHA256.HashData(Encoding.ASCII.GetBytes("abc"));
        bool c = Convert.ToHexString(h).Equals("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", StringComparison.OrdinalIgnoreCase);
        return a && b && c ? "Self-test passed: EICAR pattern detected, clean data ignored, SHA-256 verified." : $"Self-test FAILED (eicar={a}, clean={b}, sha256={c}).";
    }

    public static ScanReport Scan(string root, HashSetCompact hashes, Action<string> progress, CancellationToken ct)
    {
        var rep = new ScanReport();
        var old = Thread.CurrentThread.Priority;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;   // stay out of the user's way
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var buf = new byte[256];
            const int hashBufferSize = 65536; // 64 KB buffer for hashing
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opts))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long len = f.Length;
                    if (len == 0) continue;
                    if (len > MaxFile) { rep.SkippedLarge++; continue; }
                    bool tiny = len <= 128;
                    if (!tiny && hashes.Count == 0) { rep.Scanned++; continue; }   // nothing to compare against

                    // Use a properly buffered FileStream for efficient reading
                    using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, hashBufferSize, FileOptions.SequentialScan);

                    if (tiny)
                    {
                        int n = fs.Read(buf, 0, buf.Length);
                        if (IsEicar(buf, n)) { rep.Found.Add(new Detection(f.FullName, "EICAR-Test-File (harmless test file)", Convert.ToHexString(SHA256.HashData(new ReadOnlySpan<byte>(buf, 0, n))).ToLowerInvariant(), len)); rep.Scanned++; continue; }
                        fs.Position = 0;
                    }
                    if (hashes.Count > 0)
                    {
                        var h = SHA256.HashData(fs);
                        var key = (BitConverter.ToUInt64(new[] { h[7], h[6], h[5], h[4], h[3], h[2], h[1], h[0] }, 0), BitConverter.ToUInt64(new[] { h[15], h[14], h[13], h[12], h[11], h[10], h[9], h[8] }, 0));
                        if (hashes.Contains(key)) rep.Found.Add(new Detection(f.FullName, "Matches your malware hash list", Convert.ToHexString(h).ToLowerInvariant(), len));
                    }
                    rep.Scanned++;
                    if (rep.Scanned % 200 == 0) progress($"Scanned {rep.Scanned:N0} files…");
                }
                catch (OperationCanceledException) { throw; }
                catch { rep.Errors++; }
            }
        }
        finally { Thread.CurrentThread.Priority = old; }
        return rep;
    }
}

// Quarantine = move the file into DREX's own folder under a harmless name, remember where it came from.
// This isolates it; it is not encryption. Restore puts it back only if the original spot is free.
public static class Quarantine
{
    static string Dir => Path.Combine(Settings.DataDir, "Quarantine");
    static string Manifest => Path.Combine(Dir, "manifest.json");

    public static List<QuarItem> List()
    {
        try { if (File.Exists(Manifest)) return JsonSerializer.Deserialize<List<QuarItem>>(File.ReadAllText(Manifest)) ?? new(); } catch { }
        return new();
    }

    static void Save(List<QuarItem> l)
    {
        Directory.CreateDirectory(Dir);
        var tmp = Manifest + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(l));
        File.Move(tmp, Manifest, true);
    }

    public static (bool ok, string msg) Add(Detection d)
    {
        try
        {
            if (!File.Exists(d.Path)) return (false, "File no longer exists.");
            Directory.CreateDirectory(Dir);
            var id = Guid.NewGuid().ToString("N");
            var dest = Path.Combine(Dir, id + ".quar");
            File.SetAttributes(d.Path, FileAttributes.Normal);
            File.Move(d.Path, dest);
            var l = List(); l.Add(new QuarItem(id, d.Path, Path.GetFileName(d.Path), d.Sha256, DateTime.Now)); Save(l);
            return (true, "");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static (bool ok, string msg) Restore(QuarItem q)
    {
        try
        {
            var src = Path.Combine(Dir, q.Id + ".quar");
            if (!File.Exists(src)) return (false, "Quarantined file is missing.");
            if (!Path.IsPathRooted(q.OriginalPath)) return (false, "Invalid original path.");
            if (File.Exists(q.OriginalPath)) return (false, "A file already exists at the original location.");
            Directory.CreateDirectory(Path.GetDirectoryName(q.OriginalPath)!);
            File.Move(src, q.OriginalPath);
            Save(List().Where(x => x.Id != q.Id).ToList());
            return (true, "");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static (bool ok, string msg) Delete(QuarItem q)
    {
        try
        {
            var src = Path.Combine(Dir, q.Id + ".quar");
            if (File.Exists(src)) File.Delete(src);
            Save(List().Where(x => x.Id != q.Id).ToList());
            return (true, "");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
