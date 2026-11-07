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
        using var pen = new Pen(c, Math.Max(1, item.Style.Width)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        if (item.Tool == ToolKind.Highlighter) { pen.Color = Color.FromArgb(Math.Min(120, item.Style.Alpha), item.Style.DrawingColor); pen.Width *= 3; }
        if (item.Tool == ToolKind.Eraser) { pen.Color = Color.Magenta; pen.Width *= 4; }
        if (item.Tool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Eraser or ToolKind.Wave)
        {
            if (item.Points.Count > 1) g.DrawLines(pen, item.Tool == ToolKind.Wave ? MakeWave(item.Points) : item.Points.ToArray());
            return;
        }
        if (item.Tool is ToolKind.Person or ToolKind.Camera)
        {
            DrawMarker(g, item);
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

    public AnnotationItem? HitTestInteractive(PointF point)
    {
        using var g = CreateGraphics();
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var item = Items[i];
            if (item.Tool == ToolKind.Text && item.Points.Count > 0 && !string.IsNullOrEmpty(item.Text))
            {
                using var font = new Font("Microsoft YaHei UI", item.Style.FontSize);
                var size = g.MeasureString(item.Text, font);
                var bounds = new RectangleF(item.Points[0], size);
                bounds.Inflate(8, 8);
                if (bounds.Contains(point)) return item;
            }
            else if (item.Tool is ToolKind.Person or ToolKind.Camera && item.Points.Count > 0)
            {
                var scale = MarkerScale(item);
                if (Distance(point, item.Points[0]) <= scale * 1.5f) return item;
            }
        }
        return null;
    }

    private static void DrawMarker(Graphics g, AnnotationItem item)
    {
        var anchor = item.Points[0];
        var direction = item.Points.Count > 1 ? item.Points[^1] : new PointF(anchor.X + 1, anchor.Y);
        var angle = MathF.Atan2(direction.Y - anchor.Y, direction.X - anchor.X);
        var scale = MarkerScale(item);
        var color = Color.FromArgb(Math.Clamp(item.Style.Alpha, 0, 255), item.Style.DrawingColor);
        using var pen = new Pen(color, Math.Max(2, item.Style.Width)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(Color.FromArgb(Math.Min(72, Math.Max(28, item.Style.Alpha / 3)), item.Style.DrawingColor));
        if (item.Tool == ToolKind.Person) DrawPerson(g, anchor, angle, scale, pen, fill, item.ViewMode);
        else DrawCamera(g, anchor, angle, scale, pen, fill, item.ViewMode, direction);
    }

    private static void DrawPerson(Graphics g, PointF center, float angle, float scale, Pen pen, Brush fill, MarkerView view)
    {
        var state = g.Save();
        try
        {
            g.TranslateTransform(center.X, center.Y); g.RotateTransform(angle * 180f / MathF.PI);
            if (view == MarkerView.Top)
            {
                g.FillEllipse(fill, -scale * .22f, -scale * .62f, scale * .44f, scale * .44f); g.DrawEllipse(pen, -scale * .22f, -scale * .62f, scale * .44f, scale * .44f);
                g.FillEllipse(fill, -scale * .34f, -scale * .20f, scale * .68f, scale * .70f); g.DrawEllipse(pen, -scale * .34f, -scale * .20f, scale * .68f, scale * .70f);
                g.DrawLine(pen, -scale * .30f, scale * .05f, -scale * .70f, scale * .35f); g.DrawLine(pen, scale * .30f, scale * .05f, scale * .70f, scale * .35f);
                g.DrawLine(pen, -scale * .16f, scale * .48f, -scale * .36f, scale * .86f); g.DrawLine(pen, scale * .16f, scale * .48f, scale * .36f, scale * .86f); DrawDirectionNose(g, pen, scale * .24f);
            }
            else
            {
                g.FillEllipse(fill, -scale * .20f, -scale * .82f, scale * .40f, scale * .40f); g.DrawEllipse(pen, -scale * .20f, -scale * .82f, scale * .40f, scale * .40f);
                FillRoundedRectangle(g, fill, -scale * .28f, -scale * .40f, scale * .56f, scale * .95f, scale * .14f); DrawRoundedRectangle(g, pen, -scale * .28f, -scale * .40f, scale * .56f, scale * .95f, scale * .14f);
                g.DrawLine(pen, scale * .26f, -scale * .28f, scale * .64f, scale * .10f); g.DrawLine(pen, -scale * .08f, scale * .54f, -scale * .26f, scale * .94f); g.DrawLine(pen, scale * .08f, scale * .54f, scale * .26f, scale * .94f); DrawDirectionNose(g, pen, scale * .20f);
            }
        }
        finally { g.Restore(state); }
    }

    private static void DrawCamera(Graphics g, PointF center, float angle, float scale, Pen pen, Brush fill, MarkerView view, PointF direction)
    {
        var vectorLength = MathF.Sqrt(MathF.Pow(direction.X - center.X, 2) + MathF.Pow(direction.Y - center.Y, 2)); var viewLength = MathF.Max(scale * 2.8f, vectorLength); var halfFov = view == MarkerView.Top ? .34f : .25f;
        var state = g.Save();
        try
        {
            g.TranslateTransform(center.X, center.Y); g.RotateTransform(angle * 180f / MathF.PI);
            var lensX = scale * .52f; var cone = new PointF[] { new(lensX, -scale * .18f), new(viewLength * MathF.Cos(halfFov), viewLength * MathF.Sin(halfFov)), new(viewLength * MathF.Cos(-halfFov), viewLength * MathF.Sin(-halfFov)) };
            g.FillPolygon(fill, cone); g.DrawLine(pen, cone[0], cone[1]); g.DrawLine(pen, cone[0], cone[2]); g.DrawLine(pen, cone[1], cone[2]);
            if (view == MarkerView.Top)
            {
                FillRoundedRectangle(g, fill, -scale * .55f, -scale * .38f, scale * 1.10f, scale * .76f, scale * .12f); DrawRoundedRectangle(g, pen, -scale * .55f, -scale * .38f, scale * 1.10f, scale * .76f, scale * .12f); g.FillEllipse(fill, scale * .28f, -scale * .24f, scale * .48f, scale * .48f); g.DrawEllipse(pen, scale * .28f, -scale * .24f, scale * .48f, scale * .48f);
            }
            else
            {
                FillRoundedRectangle(g, fill, -scale * .45f, -scale * .48f, scale * .90f, scale * .76f, scale * .10f); DrawRoundedRectangle(g, pen, -scale * .45f, -scale * .48f, scale * .90f, scale * .76f, scale * .10f); g.FillEllipse(fill, scale * .40f, -scale * .25f, scale * .36f, scale * .36f); g.DrawEllipse(pen, scale * .40f, -scale * .25f, scale * .36f, scale * .36f);
            }
        }
        finally { g.Restore(state); }
    }

    private static float MarkerScale(AnnotationItem item) => Math.Clamp(Math.Max(24, item.Style.Width * 6f), 24, 120);
    private static float Distance(PointF a, PointF b) => MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));
    private static void DrawDirectionNose(Graphics g, Pen pen, float length) { g.DrawLine(pen, 0, -length * .7f, length, 0); g.DrawLine(pen, length, 0, 0, length * .7f); }
    private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        var d = radius * 2; var path = new GraphicsPath(); path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + width - d, y, d, d, 270, 90); path.AddArc(x + width - d, y + height - d, d, d, 0, 90); path.AddArc(x, y + height - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    private static void FillRoundedRectangle(Graphics g, Brush brush, float x, float y, float width, float height, float radius) { using var path = RoundedRect(x, y, width, height, radius); g.FillPath(brush, path); }
    private static void DrawRoundedRectangle(Graphics g, Pen pen, float x, float y, float width, float height, float radius) { using var path = RoundedRect(x, y, width, height, radius); g.DrawPath(pen, path); }

    private static PointF[] MakeWave(List<PointF> points)
    {
        if (points.Count < 2) return points.ToArray();
        var result = new List<PointF>();
        for (int i = 0; i < points.Count - 1; i++) { var p = points[i]; var q = points[i + 1]; var dx = q.X - p.X; var dy = q.Y - p.Y; var len = Math.Max(1, MathF.Sqrt(dx * dx + dy * dy)); var nx = -dy / len; var ny = dx / len; var mid = new PointF((p.X + q.X) / 2 + nx * 6 * (i % 2 == 0 ? 1 : -1), (p.Y + q.Y) / 2 + ny * 6 * (i % 2 == 0 ? 1 : -1)); if (i == 0) result.Add(p); result.Add(mid); result.Add(q); }
        return result.ToArray();
    }
    private static void DrawArrowHead(Graphics g, Pen pen, PointF a, PointF b)
    { var angle = Math.Atan2(b.Y - a.Y, b.X - a.X); const double size = 12; var p1 = new PointF(b.X - (float)(size * Math.Cos(angle - Math.PI / 6)), b.Y - (float)(size * Math.Sin(angle - Math.PI / 6))); var p2 = new PointF(b.X - (float)(size * Math.Cos(angle + Math.PI / 6)), b.Y - (float)(size * Math.Sin(angle + Math.PI / 6))); g.DrawLines(pen, new[] { p1, b, p2 }); }
}
