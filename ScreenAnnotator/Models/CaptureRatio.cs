using System.Globalization;

namespace ScreenAnnotator.Models;

internal readonly record struct CaptureRatio(int Width, int Height)
{
    public Size PixelStep
    {
        get
        {
            var divisor = GreatestCommonDivisor(Width, Height);
            return new Size(Width / divisor, Height / divisor);
        }
    }

    public static Rectangle Select(Point start, Point end, Size screen, CaptureRatio? ratio)
    {
        var x = Math.Clamp(end.X, 0, screen.Width);
        var y = Math.Clamp(end.Y, 0, screen.Height);
        if (ratio == null)
            return Rectangle.FromLTRB(Math.Min(start.X, x), Math.Min(start.Y, y),
                Math.Max(start.X, x), Math.Max(start.Y, y));

        var step = ratio.Value.PixelStep;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var left = dx < 0 || (dx == 0 && start.X > screen.Width - start.X);
        var up = dy < 0 || (dy == 0 && start.Y > screen.Height - start.Y);
        var availableWidth = left ? start.X : screen.Width - start.X;
        var availableHeight = up ? start.Y : screen.Height - start.Y;
        var maxSteps = Math.Min(availableWidth / step.Width, availableHeight / step.Height);
        var desiredSteps = Math.Max((double)Math.Abs(dx) / step.Width, (double)Math.Abs(dy) / step.Height);
        var steps = (int)Math.Min(maxSteps, Math.Round(desiredSteps, MidpointRounding.AwayFromZero));
        if (steps == 0) return Rectangle.Empty;

        var width = step.Width * steps;
        var height = step.Height * steps;
        return new Rectangle(left ? start.X - width : start.X, up ? start.Y - height : start.Y, width, height);
    }

    public static bool TryParse(string text, out CaptureRatio ratio)
    {
        ratio = default;
        var parts = text.Split(new[] { ':', '：' });
        if (parts.Length != 2 || !TryReadTerm(parts[0], out var width) || !TryReadTerm(parts[1], out var height))
            return false;

        var divisor = GreatestCommonDivisor(width, height);
        ratio = new CaptureRatio(width / divisor, height / divisor);
        return true;
    }

    private static bool TryReadTerm(string text, out int term)
    {
        term = 0;
        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value <= 0 || value > 10000)
            return false;

        var scaled = value * 1000;
        if (scaled != decimal.Truncate(scaled)) return false;
        term = (int)scaled;
        return true;
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
