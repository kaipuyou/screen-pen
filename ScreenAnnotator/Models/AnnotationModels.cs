using System.Drawing;
using System.Text.Json.Serialization;

namespace ScreenAnnotator.Models;

public enum ToolKind { Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Wave, Text, Eraser, Person, Camera }
public enum MarkerView { Top, Elevation }

public sealed class AnnotationStyle
{
    public string Color { get; set; } = "#E53935";
    public int Width { get; set; } = 4;
    public int Alpha { get; set; } = 255;
    public float FontSize { get; set; } = 24;
    [JsonIgnore] public Color DrawingColor => ColorTranslator.FromHtml(Color);
}

public sealed class AnnotationItem
{
    public ToolKind Tool { get; set; }
    public MarkerView ViewMode { get; set; } = MarkerView.Top;
    public List<PointF> Points { get; set; } = new();
    public AnnotationStyle Style { get; set; } = new();
    public string Text { get; set; } = string.Empty;
}

public sealed class AnnotationProject
{
    public int Version { get; set; } = 1;
    public string ScreenDevice { get; set; } = string.Empty;
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public float DpiScale { get; set; } = 1f;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<AnnotationItem> Items { get; set; } = new();
}
