using System.Runtime.InteropServices;
using System.Security.Principal;
namespace Drex;

static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    public static (ulong total, ulong avail, uint pct) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (m.ullTotalPhys, m.ullAvailPhys, m.dwMemoryLoad) : (0, 0, 0);
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    static long _pi, _pk, _pu; static bool _cpuInit;
    // CPU load between two calls (called once per 5 s tick; no counters, no polling threads).
    public static int CpuPercent()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
        long di = i - _pi, dk = k - _pk, du = u - _pu; _pi = i; _pk = k; _pu = u;
        if (!_cpuInit) { _cpuInit = true; return 0; }
        long total = dk + du;
        return total <= 0 ? 0 : (int)Math.Clamp(100 * (total - di) / total, 0, 100);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    public static void DarkTitle(IntPtr h)
    {
        try
        {
            int one = 1, cap = 0x120D0B, txt = 0xF8F5F4, bord = 0x120D0B;
            DwmSetWindowAttribute(h, 20, ref one, 4);
            DwmSetWindowAttribute(h, 35, ref cap, 4);
            DwmSetWindowAttribute(h, 36, ref txt, 4);
            DwmSetWindowAttribute(h, 34, ref bord, 4);
        }
        catch { }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string? idlist);
    public static void DarkScroll(Control c)
    {
        void Apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
        if (c.IsHandleCreated) Apply(); else c.HandleCreated += (_, _) => Apply();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo;
        public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    // Sends a file to the Recycle Bin (recoverable).
    public static bool Recycle(string path)
    {
        try
        {
            var op = new SHFILEOPSTRUCT { wFunc = 3, pFrom = path + "\0", fFlags = 0x40 | 0x10 | 0x4 | 0x400 };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
        catch { return false; }
    }

    public static bool IsAdmin()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }
}
