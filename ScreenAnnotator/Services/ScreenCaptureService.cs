using System.Drawing.Imaging;

namespace ScreenAnnotator.Services;

public static class ScreenCaptureService
{
    public static Bitmap Capture(Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }
}
