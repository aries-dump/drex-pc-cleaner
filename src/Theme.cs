using System.Drawing.Drawing2D;
namespace Drex;

public static class Theme
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#0B0D12");
    public static readonly Color Surf = ColorTranslator.FromHtml("#171B24");
    public static readonly Color Surf2 = ColorTranslator.FromHtml("#202531");
    public static readonly Color Txt = ColorTranslator.FromHtml("#F4F5F8");
    public static readonly Color Silver = ColorTranslator.FromHtml("#C4CAD5");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8B93A4");
    public static readonly Color Violet = ColorTranslator.FromHtml("#9875FF");
    public static readonly Color VioletHi = ColorTranslator.FromHtml("#B49DFF");
    public static readonly Color VioletLo = ColorTranslator.FromHtml("#7A56E8");
    public static readonly Color Good = ColorTranslator.FromHtml("#4ADE80");
    public static readonly Color Warn = ColorTranslator.FromHtml("#F2B35E");
    public static readonly Color Bad = ColorTranslator.FromHtml("#F26D7D");
    public static readonly Color Alt = ColorTranslator.FromHtml("#131720");
    public static readonly Color Sel = ColorTranslator.FromHtml("#2B2450");

    static readonly Dictionary<string, Font> _fonts = new();
    public static Font F(float size, bool bold = false) => Get(bold ? "Segoe UI Semibold" : "Segoe UI", size);
    public static Font Icons(float size) => Get("Segoe MDL2 Assets", size);
    static Font Get(string name, float size)
    {
        var k = name + "|" + size;
        if (!_fonts.TryGetValue(k, out var f)) { f = new Font(name, size, FontStyle.Regular, GraphicsUnit.Point); _fonts[k] = f; }
        return f;
    }

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        if (d <= 0 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

public static class AppIcon
{
    // The logo is embedded in the EXE so it works in single-file builds and the installed copy.
    public static Icon Get(int size)
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("drex.ico");
            if (s != null) return new Icon(s, new Size(size, size));
        }
        catch { }
        return SystemIcons.Application;
    }
}
