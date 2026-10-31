using System.Security.Cryptography;
namespace Drex;

// File encryption: AES-256-GCM in 1 MB chunks, key from password via PBKDF2-SHA256 (600k iterations).
// Every chunk is authenticated; the last chunk is flagged so truncation is detected.
public static class Vault
{
    static readonly byte[] Magic = { (byte)'D', (byte)'R', (byte)'E', (byte)'X', (byte)'E', (byte)'N', (byte)'C', (byte)'1' };
    const int Chunk = 1024 * 1024, SaltLen = 16, PrefixLen = 8, Iter = 600_000;
    static int HeaderLen => Magic.Length + SaltLen + PrefixLen + 4;

    static byte[] Key(string pw, byte[] salt, int iter) => Rfc2898DeriveBytes.Pbkdf2(pw, salt, iter, HashAlgorithmName.SHA256, 32);

    static byte[] Nonce(byte[] prefix, uint idx)
    {
        var n = new byte[12];
        Buffer.BlockCopy(prefix, 0, n, 0, PrefixLen);
        Buffer.BlockCopy(BitConverter.GetBytes(idx), 0, n, PrefixLen, 4);
        return n;
    }

    static byte[] Aad(byte[] header, bool last)
    {
        var a = new byte[header.Length + 1];
        Buffer.BlockCopy(header, 0, a, 0, header.Length);
        a[header.Length] = last ? (byte)1 : (byte)0;
        return a;
    }

    static int ReadFull(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length) { int r = s.Read(buf, total, buf.Length - total); if (r <= 0) break; total += r; }
        return total;
    }

    public static void Encrypt(string src, string dest, string password, Action<int> progress, CancellationToken ct)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var prefix = RandomNumberGenerator.GetBytes(PrefixLen);
        var key = Key(password, salt, Iter);
        var header = new byte[HeaderLen];
        Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
        Buffer.BlockCopy(salt, 0, header, Magic.Length, SaltLen);
        Buffer.BlockCopy(prefix, 0, header, Magic.Length + SaltLen, PrefixLen);
        Buffer.BlockCopy(BitConverter.GetBytes(Iter), 0, header, Magic.Length + SaltLen + PrefixLen, 4);

        using var inp = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        using var outp = new FileStream(dest, FileMode.CreateNew, FileAccess.Write);   // CreateNew: never overwrites
        outp.Write(header, 0, header.Length);
        using var aes = new AesGcm(key, 16);
        var buf = new byte[Chunk]; var next = new byte[Chunk];
        int n = ReadFull(inp, buf); uint idx = 0; long total = Math.Max(1, inp.Length), done = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n2 = n == Chunk ? ReadFull(inp, next) : 0;
            bool last = n2 == 0;
            var cipher = new byte[n]; var tag = new byte[16];
            aes.Encrypt(Nonce(prefix, idx), new ReadOnlySpan<byte>(buf, 0, n), cipher, tag, Aad(header, last));
            outp.Write(BitConverter.GetBytes(n), 0, 4); outp.Write(cipher, 0, n); outp.Write(tag, 0, 16);
            done += n; progress((int)(done * 100 / total));
            if (last) break;
            (buf, next) = (next, buf); n = n2; idx++;
        }
        outp.Flush(true);
        CryptographicOperations.ZeroMemory(key);
    }

    public static void Decrypt(string src, string dest, string password, Action<int> progress, CancellationToken ct)
    {
        using var inp = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        var header = new byte[HeaderLen];
        if (inp.Length < HeaderLen + 20 || ReadFull(inp, header) != HeaderLen) throw new InvalidDataException("This is not a DREX encrypted file.");
        for (int i = 0; i < Magic.Length; i++) if (header[i] != Magic[i]) throw new InvalidDataException("This is not a DREX encrypted file.");
        var salt = new byte[SaltLen]; var prefix = new byte[PrefixLen];
        Buffer.BlockCopy(header, Magic.Length, salt, 0, SaltLen);
        Buffer.BlockCopy(header, Magic.Length + SaltLen, prefix, 0, PrefixLen);
        int iter = BitConverter.ToInt32(header, Magic.Length + SaltLen + PrefixLen);
        if (iter < 100_000 || iter > 10_000_000) throw new InvalidDataException("The file header is damaged.");
        var key = Key(password, salt, iter);

        using var outp = new FileStream(dest, FileMode.CreateNew, FileAccess.Write);
        using var aes = new AesGcm(key, 16);
        var lenBuf = new byte[4]; var tag = new byte[16]; uint idx = 0; long total = Math.Max(1, inp.Length);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            inp.ReadExactly(lenBuf, 0, 4);
            int n = BitConverter.ToInt32(lenBuf, 0);
            if (n < 0 || n > Chunk) throw new InvalidDataException("The file is damaged.");
            var cipher = new byte[n]; inp.ReadExactly(cipher, 0, n); inp.ReadExactly(tag, 0, 16);
            bool last = inp.Position >= inp.Length;
            var plain = new byte[n];
            try { aes.Decrypt(Nonce(prefix, idx), cipher, tag, plain, Aad(header, last)); }
            catch (CryptographicException) { throw new CryptographicException("Wrong password, or the file was modified."); }
            outp.Write(plain, 0, n); idx++;
            progress((int)(inp.Position * 100 / total));
            if (last) break;
        }
        outp.Flush(true);
        CryptographicOperations.ZeroMemory(key);
    }

    // One random-data overwrite pass, then delete. Cannot be guaranteed on SSDs, cloud-synced folders or backups.
    public static void Shred(string path, Action<int> progress, CancellationToken ct)
    {
        var fi = new FileInfo(path);
        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Links are not shredded.");
        fi.Attributes = FileAttributes.Normal;
        long len = fi.Length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var buf = new byte[81920]; long done = 0;
            while (done < len)
            {
                ct.ThrowIfCancellationRequested();
                RandomNumberGenerator.Fill(buf);
                int n = (int)Math.Min(buf.Length, len - done);
                fs.Write(buf, 0, n); done += n;
                progress((int)(done * 100 / Math.Max(1, len)));
            }
            fs.Flush(true);
        }
        File.Delete(path);
    }
}
