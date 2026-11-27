using ScreenAnnotator.Models;
using System.Drawing.Drawing2D;

namespace ScreenAnnotator;

public sealed class DrawingCanvas : Control
{
    public List<AnnotationItem> Items { get; } = new();
    public ToolKind Tool { get; set; } = ToolKind.Pen;
    public AnnotationStyle Style { get; set; } = new();
    public PointF? StartPoint { get; set; }
    public PointF? CurrentPoint { get; set; }
    public AnnotationItem? PreviewItem { get; set; }

    public DrawingCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Magenta;
        Dock = DockStyle.Fill;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (var item in Items) DrawItem(e.Graphics, item);
        if (PreviewItem != null) DrawItem(e.Graphics, PreviewItem);
    }

    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        if (m.Msg == WmNcHitTest)
        {
            m.Result = (IntPtr)HtTransparent;
            return;
        }
        base.WndProc(ref m);
    }

    public void DrawItem(Graphics g, AnnotationItem item)
    {
        if (item.Points.Count == 0) return;
        var c = Color.FromArgb(Math.Clamp(item.Style.Alpha, 0, 255), item.Style.DrawingColor);
        if (item.Tool is ToolKind.Person or ToolKind.Camera)
        {
            DrawMarker(g, item, c);
            return;
        }
        using var pen = new Pen(c, Math.Max(1, item.Style.Width)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        if (item.Tool == ToolKind.Highlighter) { pen.Color = Color.FromArgb(Math.Min(120, item.Style.Alpha), item.Style.DrawingColor); pen.Width *= 3; }
        if (item.Tool == ToolKind.Eraser) { pen.Color = Color.Magenta; pen.Width *= 4; }
        if (item.Tool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Eraser or ToolKind.Wave)
        {
            if (item.Points.Count > 1) g.DrawLines(pen, item.Tool == ToolKind.Wave ? MakeWave(item.Points) : item.Points.ToArray());
            return;
        }
        var a = item.Points[0]; var b = item.Points[^1]; var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        if (item.Tool == ToolKind.Line) g.DrawLine(pen, a, b);
        else if (item.Tool == ToolKind.Arrow) { g.DrawLine(pen, a, b); DrawArrowHead(g, pen, a, b); }
        else if (item.Tool == ToolKind.Rectangle) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        else if (item.Tool == ToolKind.Ellipse) g.DrawEllipse(pen, r);
        else if (item.Tool == ToolKind.Text) using (var brush = new SolidBrush(c)) using (var font = new Font("Microsoft YaHei UI", item.Style.FontSize)) g.DrawString(item.Text, font, brush, a);
    }

    public AnnotationItem? HitTestText(PointF point)
    {
        using var g = CreateGraphics();
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var item = Items[i];
            if (item.Tool != ToolKind.Text || item.Points.Count == 0 || string.IsNullOrEmpty(item.Text)) continue;
            using var font = new Font("Microsoft YaHei UI", item.Style.FontSize);
            var size = g.MeasureString(item.Text, font);
            var bounds = new RectangleF(item.Points[0], size);
            bounds.Inflate(8, 8);
            if (bounds.Contains(point)) return item;
        }
        return null;
    }

    public AnnotationItem? HitTestInteractive(PointF point, ToolKind tool)
    {
        if (tool == ToolKind.Text) return HitTestText(point);

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var item = Items[i];
            if (item.Tool == tool && item.Points.Count > 0 &&
                Distance(point, item.Points[0]) <= MarkerScale(item) * 1.15f)
                return item;
        }
        return null;
    }

    private static void DrawMarker(Graphics g, AnnotationItem item, Color color)
    {
        var anchor = item.Points[0];
        var direction = item.Points[^1];
        var length = Distance(direction, anchor);
        var topPerson = item.Tool == ToolKind.Person && item.ViewMode == MarkerView.Top;
        var angle = length < 2 ? 0 : MathF.Atan2(direction.Y - anchor.Y, direction.X - anchor.X) * 180f / MathF.PI;
        if (topPerson && length >= 2) angle -= 90;
        var scale = MarkerScale(item);
        using var pen = new Pen(color, Math.Clamp(item.Style.Width / scale, .085f, .14f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var state = g.Save();
        try
        {
            g.TranslateTransform(anchor.X, anchor.Y);
            g.RotateTransform(angle);
            g.ScaleTransform(scale, scale);
            if (item.Tool == ToolKind.Camera)
                DrawCamera(g, pen, item.ViewMode, Math.Max(2.8f, length / scale));
            else
                DrawPerson(g, pen, item.ViewMode);
        }
        finally { g.Restore(state); }
    }

    private static void DrawPerson(Graphics g, Pen pen, MarkerView view)
    {
        if (view == MarkerView.Top)
        {
            // 头部在肩线前方，肩线在头部两侧收口。
            using var shoulders = new GraphicsPath();
            shoulders.AddBezier(-.37f, .14f, -1.27f, .03f, -1.02f, -.64f, 0, -.64f);
            shoulders.AddBezier(0, -.64f, 1.02f, -.64f, 1.27f, .03f, .37f, .14f);
            g.DrawPath(pen, shoulders);
            g.DrawEllipse(pen, -.37f, -.22f, .74f, .68f);
            g.DrawEllipse(pen, -.94f, -.01f, .20f, .75f);
            g.DrawEllipse(pen, .74f, -.01f, .20f, .75f);
            return;
        }

        g.DrawEllipse(pen, -.30f, -1.00f, .60f, .88f);
        g.DrawLine(pen, -.14f, -.67f, .14f, -.67f);
        g.DrawLine(pen, -.13f, -.48f, -.08f, -.48f);
        g.DrawLine(pen, .08f, -.48f, .13f, -.48f);
        g.DrawLine(pen, 0, -.12f, 0, .32f);
        g.DrawLine(pen, -.61f, .14f, .61f, .14f);
        g.DrawBezier(pen, 0, .32f, -.06f, .69f, -.34f, .91f, -.61f, 1.03f);
        g.DrawBezier(pen, 0, .32f, .06f, .69f, .34f, .91f, .61f, 1.03f);
    }

    private static void DrawCamera(Graphics g, Pen pen, MarkerView view, float length)
    {
        g.DrawRectangle(pen, -.83f, -.35f, 1.42f, .75f);
        g.DrawLines(pen, new PointF[] { new(.59f, -.17f), new(.92f, -.17f), new(.92f, .22f), new(.59f, .22f) });
        if (view == MarkerView.Elevation)
        {
            g.DrawEllipse(pen, -.69f, -.95f, .60f, .60f);
            g.DrawEllipse(pen, .09f, -.78f, .43f, .43f);
            g.DrawLines(pen, new PointF[] { new(-.67f, .40f), new(-.67f, .75f), new(-.25f, .75f), new(-.25f, .40f) });
        }
        else
        {
            using var detailPen = (Pen)pen.Clone();
            detailPen.Width *= .72f;
            g.DrawRectangle(detailPen, -.66f, -.17f, .62f, .20f);
            g.DrawRectangle(detailPen, .12f, .12f, .28f, .12f);
        }

        var lens = new PointF(.92f, .025f);
        var halfAngle = view == MarkerView.Top ? .43f : .37f;
        var spread = (length - lens.X) * MathF.Tan(halfAngle);
        DrawViewRay(g, pen, lens, new PointF(length, lens.Y - spread));
        DrawViewRay(g, pen, lens, new PointF(length, lens.Y + spread));
    }

    private static void DrawViewRay(Graphics g, Pen pen, PointF lens, PointF tip)
    {
        g.DrawLine(pen, lens, tip);
        DrawArrowHead(g, pen, lens, tip, .32f);
    }

    private static float MarkerScale(AnnotationItem item) => 16 + Math.Clamp(item.Style.Width, 1, 50) * 2f;
    private static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static PointF[] MakeWave(List<PointF> points)
    {
        if (points.Count < 2) return points.ToArray();
        var result = new List<PointF>();
        for (int i = 0; i < points.Count - 1; i++) { var p = points[i]; var q = points[i + 1]; var dx = q.X - p.X; var dy = q.Y - p.Y; var len = Math.Max(1, MathF.Sqrt(dx * dx + dy * dy)); var nx = -dy / len; var ny = dx / len; var mid = new PointF((p.X + q.X) / 2 + nx * 6 * (i % 2 == 0 ? 1 : -1), (p.Y + q.Y) / 2 + ny * 6 * (i % 2 == 0 ? 1 : -1)); if (i == 0) result.Add(p); result.Add(mid); result.Add(q); }
        return result.ToArray();
    }
    private static void DrawArrowHead(Graphics g, Pen pen, PointF a, PointF b, float size = 12)
    {
        var angle = MathF.Atan2(b.Y - a.Y, b.X - a.X);
        var left = angle - MathF.PI / 6;
        var right = angle + MathF.PI / 6;
        var p1 = new PointF(b.X - size * MathF.Cos(left), b.Y - size * MathF.Sin(left));
        var p2 = new PointF(b.X - size * MathF.Cos(right), b.Y - size * MathF.Sin(right));
        g.DrawLines(pen, new[] { p1, b, p2 });
    }
}
