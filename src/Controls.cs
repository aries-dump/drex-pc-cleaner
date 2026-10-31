using System.Drawing.Drawing2D;
using System.Globalization;
namespace Drex;

public class Card : Panel
{
    public bool Accent;
    public int Radius = 14;
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surf;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var br = new SolidBrush(Theme.Surf);
        g.FillPath(br, path);
        using var pen = new Pen(Accent ? Color.FromArgb(110, Theme.Violet) : Theme.Surf2, 1f);
        g.DrawPath(pen, path);
    }
}

public class GButton : Control
{
    public bool Primary;
    bool _hover, _down;
    public GButton(string text, bool primary = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text; Primary = primary; Font = Theme.F(9.5f, true); Cursor = Cursors.Hand;
        Size = new Size(Math.Max(104, TextRenderer.MeasureText(text, Font).Width + 44), 44);
        Margin = new Padding(0, 0, 6, 0);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(4, 4, Width - 9, Height - 9);
        using var path = Theme.Round(r, 10);
        Color fore;
        if (Primary && Enabled)
        {
            for (int i = 1; i <= 3; i++)
            {
                using var gp = Theme.Round(Rectangle.Inflate(r, i, i), 10 + i);
                using var pen = new Pen(Color.FromArgb((_hover ? 46 : 26) / i, Theme.Violet), 1.2f);
                g.DrawPath(pen, gp);
            }
            using var lg = new LinearGradientBrush(r, _hover ? Theme.VioletHi : Theme.Violet, Theme.VioletLo, 90f);
            g.FillPath(lg, path);
            fore = Color.White;
        }
        else
        {
            var fill = !Enabled ? Theme.Surf : _down ? Color.FromArgb(0x35, 0x3C, 0x50) : _hover ? Color.FromArgb(0x2B, 0x31, 0x42) : Theme.Surf2;
            using var br = new SolidBrush(fill);
            g.FillPath(br, path);
            using var bp = new Pen(Color.FromArgb(0x2D, 0x34, 0x46));
            g.DrawPath(bp, path);
            fore = Enabled ? Theme.Txt : Theme.Muted;
        }
        TextRenderer.DrawText(g, Text, Font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

public class Toggle : Control
{
    bool _on;
    public event EventHandler? CheckedChanged;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _on;
        set { if (_on == value) return; _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(48, 28); Cursor = Cursors.Hand;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = Theme.Round(r, r.Height / 2);
        using var br = new SolidBrush(_on ? Theme.Violet : Color.FromArgb(0x2B, 0x31, 0x42));
        g.FillPath(br, path);
        int d = r.Height - 8;
        int x = _on ? r.Right - d - 4 : r.X + 4;
        using var kb = new SolidBrush(_on ? Color.White : Theme.Silver);
        g.FillEllipse(kb, x, r.Y + 4, d, d);
    }
}

public class DarkBar : Control
{
    int _v;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value { get => _v; set { var n = Math.Clamp(value, 0, 100); if (n != _v) { _v = n; Invalidate(); } } }
    public DarkBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tr = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var tp = Theme.Round(tr, Height / 2)) using (var tb = new SolidBrush(Theme.Surf2)) g.FillPath(tb, tp);
        int w = (int)((Width - 1) * _v / 100.0);
        if (w >= Height)
        {
            var fr = new Rectangle(0, 0, w, Height - 1);
            Color a = _v >= 90 ? Theme.Bad : _v >= 75 ? Theme.Warn : Theme.VioletLo;
            Color b = _v >= 90 ? Theme.Bad : _v >= 75 ? Theme.Warn : Theme.VioletHi;
            using var fp = Theme.Round(fr, Height / 2);
            using var lg = new LinearGradientBrush(fr, a, b, 0f);
            g.FillPath(lg, fp);
        }
    }
}

public class Ring : Control
{
    float _v; string _big = "", _small = "";
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Value { get => _v; set { if (Math.Abs(_v - value) > 0.01f) { _v = value; Invalidate(); } } }
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Big { get => _big; set { if (_big != value) { _big = value; Invalidate(); } } }
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Small { get => _small; set { if (_small != value) { _small = value; Invalidate(); } } }
    public Ring()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(110, 110);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(9, 9, Width - 19, Height - 19);
        using (var track = new Pen(Theme.Surf2, 10f)) g.DrawEllipse(track, r);
        float sweep = 360f * Math.Clamp(_v, 0f, 100f) / 100f;
        if (sweep > 1f)
        {
            var col = _v >= 90 ? Theme.Bad : _v >= 75 ? Theme.Warn : Theme.Violet;
            using var pen = new Pen(col, 10f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, r, -90f, sweep);
        }
        TextRenderer.DrawText(g, _big, Theme.F(Width > 130 ? 20f : 15f, true), new Rectangle(0, Height / 2 - 24, Width, 30), Theme.Txt, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _small, Theme.F(8.5f), new Rectangle(0, Height / 2 + 6, Width, 18), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public class NavItem : Control
{
    public string Glyph = "";
    bool _sel, _hover;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _sel; set { if (_sel != value) { _sel = value; Invalidate(); } } }
    public NavItem(string text, string glyph)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text; Glyph = glyph; Size = new Size(212, 34); Cursor = Cursors.Hand; Font = Theme.F(10f);
        Margin = Padding.Empty; BackColor = Theme.Surf;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surf);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(10, 3, Width - 20, Height - 7);
        if (_sel || _hover)
        {
            using var p = Theme.Round(r, 10);
            using var br = new SolidBrush(_sel ? Color.FromArgb(0x22, 0x27, 0x36) : Color.FromArgb(0x1C, 0x21, 0x2D));
            g.FillPath(br, p);
        }
        if (_sel)
        {
            using var glow = new SolidBrush(Color.FromArgb(34, Theme.Violet));
            g.FillRectangle(glow, 0, 9, 8, Height - 18);
            using var bar = new SolidBrush(Theme.Violet);
            g.FillRectangle(bar, 0, 10, 3, Height - 20);
        }
        TextRenderer.DrawText(g, Glyph, Theme.Icons(12f), new Rectangle(22, 0, 26, Height), _sel ? Theme.Violet : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(58, 0, Width - 62, Height), _sel ? Theme.Txt : Theme.Silver, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public class DarkList : ListView
{
    public int BarColumn = -1;
    public DarkList(params (string name, int width)[] cols)
    {
        View = View.Details; FullRowSelect = true; OwnerDraw = true; BorderStyle = BorderStyle.None;
        BackColor = Theme.Surf; ForeColor = Theme.Silver; HideSelection = false; MultiSelect = true; Font = Theme.F(9.5f);
        DoubleBuffered = true; HeaderStyle = ColumnHeaderStyle.Nonclickable;
        SmallImageList = new ImageList { ImageSize = new Size(1, 34) };
        foreach (var (n, w) in cols) Columns.Add(n, w);
        Native.DarkScroll(this);
        HandleCreated += (_, _) => FitLast();
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); FitLast(); }
    public void FitLast()
    {
        if (Columns.Count == 0 || !IsHandleCreated) return;
        int used = 0;
        for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
        int w = ClientSize.Width - used;
        if (w > 60) Columns[Columns.Count - 1].Width = w;
    }
    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using (var b = new SolidBrush(Theme.Bg)) e.Graphics.FillRectangle(b, e.Bounds);
        using (var p = new Pen(Theme.Surf2)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var r = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(10, e.Bounds.Width - 12), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Theme.F(8.5f, true), r, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    protected override void OnDrawItem(DrawListViewItemEventArgs e) { }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics; var it = e.Item; if (it == null) return;
        var r = e.Bounds;
        if (e.ColumnIndex == 0) r = new Rectangle(r.X, r.Y, Columns[0].Width, r.Height);
        using (var bg = new SolidBrush(it.Selected ? Theme.Sel : (e.ItemIndex % 2 == 0 ? Theme.Surf : Theme.Alt))) g.FillRectangle(bg, r);
        string text = e.SubItem?.Text ?? "";
        if (e.ColumnIndex == BarColumn)
        {
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var tr = new Rectangle(r.X + 10, r.Y + r.Height / 2 - 4, Math.Max(20, r.Width - 74), 8);
            using (var tp = Theme.Round(tr, 4)) using (var tb = new SolidBrush(Theme.Surf2)) g.FillPath(tb, tp);
            int w = (int)(tr.Width * Math.Clamp(pct, 0, 100) / 100.0);
            if (w >= 8)
            {
                var fr = new Rectangle(tr.X, tr.Y, w, tr.Height);
                using var fp = Theme.Round(fr, 4);
                using var lg = new LinearGradientBrush(fr, Theme.VioletLo, Theme.VioletHi, 0f);
                g.FillPath(lg, fp);
            }
            TextRenderer.DrawText(g, pct.ToString("0.#", CultureInfo.InvariantCulture) + "%", Theme.F(8.5f), new Rectangle(tr.Right + 4, r.Y, 56, r.Height), Theme.Silver, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            return;
        }
        Color raw = it.UseItemStyleForSubItems ? it.ForeColor : (e.SubItem?.ForeColor ?? ForeColor);
        Color c = raw.ToArgb() != ForeColor.ToArgb() ? raw : (e.ColumnIndex == 0 ? Theme.Txt : Theme.Silver);
        TextRenderer.DrawText(g, text, Theme.F(9.5f), new Rectangle(r.X + 10, r.Y, Math.Max(10, r.Width - 14), r.Height), c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
