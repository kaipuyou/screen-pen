using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenAnnotator.Services;

public static class ScreenCaptureService
{
    private const uint CaptureCompositedWindows = 0x40CC0020; // SRCCOPY | CAPTUREBLT

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);

    public static Bitmap Capture(Rectangle bounds)
    {
        // 等待透明标注层和隐藏的工具栏完成合成，再冻结画面。
        _ = DwmFlush();
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            CopyDesktop(graphics, bounds);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void CopyDesktop(Graphics graphics, Rectangle bounds)
    {
        var desktop = GetDC(IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new InvalidOperationException("无法读取桌面画面。");
        try
        {
            var destination = graphics.GetHdc();
            try
            {
                if (!BitBlt(destination, 0, 0, bounds.Width, bounds.Height, desktop,
                        bounds.X, bounds.Y, CaptureCompositedWindows))
                    throw new Win32Exception();
            }
            finally { graphics.ReleaseHdc(destination); }
        }
        finally { ReleaseDC(IntPtr.Zero, desktop); }
    }
}
