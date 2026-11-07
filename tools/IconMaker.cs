using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconMaker
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "ScreenAnnotator.ico";
        using (var bitmap = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0))) g.FillEllipse(shadow, 35, 205, 180, 24);
            using (var monitor = new SolidBrush(Color.FromArgb(22, 79, 132))) g.FillRoundedRectangle(monitor, new Rectangle(25, 36, 190, 146), 24);
            using (var screen = new LinearGradientBrush(new Rectangle(41, 52, 158, 112), Color.FromArgb(39, 190, 235), Color.FromArgb(21, 112, 201), 35f)) g.FillRoundedRectangle(screen, new Rectangle(41, 52, 158, 112), 15);
            using (var shine = new Pen(Color.FromArgb(120, 255, 255, 255), 7)) g.DrawArc(shine, 52, 64, 118, 75, 205, 95);
            using (var stand = new SolidBrush(Color.FromArgb(18, 61, 105))) { g.FillRectangle(stand, 105, 180, 30, 27); g.FillRoundedRectangle(stand, new Rectangle(72, 202, 96, 18), 8); }
            using (var stroke = new Pen(Color.FromArgb(255, 91, 46), 17) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawBezier(stroke, 61, 142, 99, 112, 139, 157, 202, 93);
            var penPoints = new[] { new PointF(180, 30), new PointF(226, 76), new PointF(201, 101), new PointF(155, 55) };
            using (var penBody = new SolidBrush(Color.FromArgb(244, 72, 43))) g.FillPolygon(penBody, penPoints);
            using (var penLight = new Pen(Color.FromArgb(255, 177, 145), 7)) g.DrawLine(penLight, 185, 43, 213, 71);
            using (var tip = new SolidBrush(Color.FromArgb(255, 214, 160))) g.FillPolygon(tip, new[] { new PointF(155, 55), new PointF(166, 90), new PointF(201, 101) });
            using (var nib = new SolidBrush(Color.FromArgb(39, 49, 61))) g.FillPolygon(nib, new[] { new PointF(155, 55), new PointF(160, 73), new PointF(174, 69) });
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            var data = png.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            using var file = new BinaryWriter(File.Create(output));
            file.Write((ushort)0); file.Write((ushort)1); file.Write((ushort)1);
            file.Write((byte)0); file.Write((byte)0); file.Write((byte)0); file.Write((byte)0);
            file.Write((ushort)1); file.Write((ushort)32); file.Write(data.Length); file.Write(22);
            file.Write(data);
        }
    }

    private static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
