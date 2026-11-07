using ScreenAnnotator.Models;

namespace ScreenAnnotator;

public sealed class OverlayForm : Form
{
    private static readonly (Keys Key, string Label, string Html)[] BasicColors =
    {
        (Keys.NumPad0, "0 黑", "#111111"),
        (Keys.NumPad1, "1 红", "#E53935"),
        (Keys.NumPad2, "2 橙", "#FB8C00"),
        (Keys.NumPad3, "3 黄", "#FDD835"),
        (Keys.NumPad4, "4 绿", "#43A047"),
        (Keys.NumPad5, "5 青", "#00ACC1"),
        (Keys.NumPad6, "6 蓝", "#1E88E5"),
        (Keys.NumPad7, "7 紫", "#8E24AA"),
        (Keys.NumPad8, "8 粉", "#EC407A"),
        (Keys.NumPad9, "9 白", "#FFFFFF")
    };
    private readonly DrawingCanvas canvas = new();
    private readonly Panel toolbar = new();
    private readonly MainForm owner;
    private readonly Dictionary<ToolKind, AnnotationStyle> toolStyles = new();
    private NumericUpDown? sizeControl;
    private Label? sizeHint;
    private InputCaptureForm? inputLayer;
    private PointF start;
    private bool drawing;
    private AnnotationItem? movingText;
    private AnnotationItem? movingMarker;
    private AnnotationItem? selectedMarker;
    private AnnotationItem? selectedText;
    private PointF textDragOffset;
    private PointF markerDragOffset;
    private MarkerView markerView = MarkerView.Top;
    private Button? viewButton;
    public ToolKind CurrentTool
    {
        get => canvas.Tool;
        set
        {
            toolStyles[canvas.Tool] = CloneStyle(canvas.Style);
            canvas.Tool = value;
            if (value != ToolKind.Text) selectedText = null;
            if (value is not (ToolKind.Person or ToolKind.Camera)) selectedMarker = null;
            if (toolStyles.TryGetValue(value, out var style)) canvas.Style = CloneStyle(style);
            if (value is ToolKind.Person or ToolKind.Camera && selectedMarker?.Tool == value)
            {
                markerView = selectedMarker.ViewMode;
            }
            UpdateSizeDisplay();
        }
    }
    public bool ToolbarVisible { get => toolbar.Visible; set => toolbar.Visible = value; }
    public bool IsEditing => inputLayer?.Visible == true;
    public IReadOnlyList<AnnotationItem> Items => canvas.Items;
    public AnnotationItem? RemoveLastItem()
    {
        if (canvas.Items.Count == 0) return null;
        var item = canvas.Items[^1];
        canvas.Items.RemoveAt(canvas.Items.Count - 1);
        canvas.Invalidate();
        return item;
    }
    public void AddItem(AnnotationItem item) { canvas.Items.Add(item); canvas.Invalidate(); }
    public void SetEditing(bool editing)
    {
        if (inputLayer == null) return;
        if (editing)
        {
            TopMost = true;
            Show();
            inputLayer.Show();
            toolbar.Visible = true;
            BringToFront();
            Activate();
        }
        else
        {
            inputLayer.Hide();
            toolbar.Visible = false;
            // 暂停后彻底隐藏覆盖窗，把桌面、任务栏和其他程序完全还给用户。
            TopMost = false;
            Hide();
        }
    }

    public OverlayForm(MainForm owner, Screen screen)
    {
        foreach (ToolKind tool in Enum.GetValues<ToolKind>()) toolStyles[tool] = DefaultStyle(tool);
        canvas.Style = CloneStyle(toolStyles[ToolKind.Pen]);
        this.owner = owner; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = screen.Bounds; TopMost = true; ShowInTaskbar = false; BackColor = Color.Magenta; TransparencyKey = Color.Magenta; KeyPreview = true;
        canvas.Parent = this; canvas.BringToFront(); BuildToolbar();
        KeyDown += OverlayKeyDown;
        Shown += (_, _) => CreateInputLayer();
    }

    private void CreateInputLayer()
    {
        inputLayer = new InputCaptureForm(Bounds);
        inputLayer.MouseDown += OverlayMouseDown;
        inputLayer.MouseMove += OverlayMouseMove;
        inputLayer.MouseUp += OverlayMouseUp;
        inputLayer.KeyDown += OverlayKeyDown;
        inputLayer.Show();
        BringToFront();
        Activate();
    }
    private void BuildToolbar()
    {
        toolbar.BackColor = Color.FromArgb(235, 32, 36, 45);
        toolbar.ForeColor = Color.White;
        toolbar.Height = 172;
        toolbar.Width = Math.Min(1100, Math.Max(520, ClientSize.Width - 24));
        toolbar.Left = 12;
        toolbar.Top = 12;
        toolbar.Visible = true;
        toolbar.Parent = this;
        toolbar.BringToFront();

        // 工具按钮使用可换行布局：小屏幕上“人形 I / 相机 C”不会再被裁到屏幕外。
        var tools = new (string, ToolKind)[]
        {
            ("画笔 P", ToolKind.Pen), ("荧光 H", ToolKind.Highlighter), ("直线 L", ToolKind.Line),
            ("箭头 A", ToolKind.Arrow), ("矩形 R", ToolKind.Rectangle), ("椭圆 O", ToolKind.Ellipse),
            ("波浪 W", ToolKind.Wave), ("文字 T", ToolKind.Text), ("橡皮 E", ToolKind.Eraser),
            ("人形 I", ToolKind.Person), ("相机 C", ToolKind.Camera)
        };
        var toolPanel = new FlowLayoutPanel
        {
            Left = 6, Top = 5, Width = toolbar.Width - 12, Height = 72,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
            AutoScroll = false, Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        foreach (var t in tools)
        {
            var b = new Button
            {
                Text = t.Item1, Width = 76, Height = 34, FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White, Margin = new Padding(1, 1, 1, 1), TabStop = false
            };
            b.Click += (_, _) => CurrentTool = t.Item2;
            toolPanel.Controls.Add(b);
        }
        toolbar.Controls.Add(toolPanel);

        int x = 6;
        const int settingsTop = 82;
        var color = new Button { Text = "颜色", Width = 62, Height = 34, Left = x, Top = settingsTop };
        color.Click += (_, _) => { using var d = new ColorDialog(); if (d.ShowDialog() == DialogResult.OK) { canvas.Style.Color = ColorTranslator.ToHtml(d.Color); toolStyles[CurrentTool] = CloneStyle(canvas.Style); } };
        toolbar.Controls.Add(color); x += 68;
        sizeControl = new NumericUpDown { Minimum = 1, Maximum = 120, Value = 4, Width = 52, Left = x, Top = settingsTop + 7 };
        sizeControl.ValueChanged += (_, _) =>
        {
            if (CurrentTool == ToolKind.Text) canvas.Style.FontSize = (float)sizeControl.Value;
            else canvas.Style.Width = Math.Min(50, (int)sizeControl.Value);
            toolStyles[CurrentTool] = CloneStyle(canvas.Style);
            UpdateSizeHint();
        };
        toolbar.Controls.Add(sizeControl); x += 52;
        sizeHint = new Label { Text = "大小 4  ↑↓←→", AutoSize = false, Width = 160, Height = 32, Left = x, Top = settingsTop + 2, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White }; toolbar.Controls.Add(sizeHint); x += 164;
        var shot = new Button { Text = "截图 F3", Width = 140, Height = 34, Left = x, Top = settingsTop }; shot.Click += (_, _) => owner.BeginRegionCapture(); toolbar.Controls.Add(shot); x += 146;
        var hideToolbar = new Button { Text = "隐藏 Tab", Width = 92, Height = 34, Left = x, Top = settingsTop, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        hideToolbar.Click += (_, _) => toolbar.Visible = false;
        toolbar.Controls.Add(hideToolbar);
        x += 98;
        viewButton = new Button { Text = "俯视 V", Width = 92, Height = 34, Left = x, Top = settingsTop, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        viewButton.Click += (_, _) => ToggleMarkerView();
        toolbar.Controls.Add(viewButton);
        UpdateViewButton();
        x = 6;
        foreach (var preset in BasicColors)
        {
            var swatchColor = ColorTranslator.FromHtml(preset.Html);
            var swatch = new Button
            {
                Text = preset.Label,
                Width = 72,
                Height = 32,
                Left = x,
                Top = 132,
                FlatStyle = FlatStyle.Flat,
                BackColor = swatchColor,
                ForeColor = preset.Key is Keys.NumPad3 or Keys.NumPad9 ? Color.Black : Color.White
            };
            swatch.Click += (_, _) => SetCurrentColor(preset.Html);
            toolbar.Controls.Add(swatch);
            x += 74;
        }
    }
    private void OverlayKeyDown(object? s, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) owner.PauseOverlay();
        else if (e.KeyCode == Keys.Tab) toolbar.Visible = !toolbar.Visible;
        else if (e.KeyCode == Keys.V) ToggleMarkerView();
        else if (e.Control && e.KeyCode == Keys.Z) owner.Undo();
        else if (e.Control && e.KeyCode == Keys.Y) owner.Redo();
        else if (e.Control && e.KeyCode == Keys.S) owner.SaveProject();
        else if (e.KeyCode == Keys.Up) ChangeToolSize(1);
        else if (e.KeyCode == Keys.Down) ChangeToolSize(-1);
        else if (e.KeyCode == Keys.Right) ChangeToolSize(5);
        else if (e.KeyCode == Keys.Left) ChangeToolSize(-5);
        else if (BasicColors.FirstOrDefault(preset => preset.Key == e.KeyCode) is var preset && preset.Key == e.KeyCode) SetCurrentColor(preset.Html);
        else if (!e.Control && !e.Alt)
        {
            CurrentTool = e.KeyCode switch
            {
                Keys.P => ToolKind.Pen,
                Keys.H => ToolKind.Highlighter,
                Keys.L => ToolKind.Line,
                Keys.A => ToolKind.Arrow,
                Keys.R => ToolKind.Rectangle,
                Keys.O => ToolKind.Ellipse,
                Keys.W => ToolKind.Wave,
                Keys.T => ToolKind.Text,
                Keys.E => ToolKind.Eraser,
                Keys.I => ToolKind.Person,
                Keys.C => ToolKind.Camera,
                _ => CurrentTool
            };
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }
    private void OverlayMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || toolbar.Bounds.Contains(e.Location)) return;
        if (CurrentTool is ToolKind.Text or ToolKind.Person or ToolKind.Camera)
        {
            var existing = canvas.HitTestInteractive(e.Location);
            if (existing != null && existing.Tool == CurrentTool)
            {
                if (CurrentTool == ToolKind.Text)
                {
                    movingText = existing;
                    selectedText = existing;
                    canvas.Style.FontSize = existing.Style.FontSize;
                    canvas.Style.Color = existing.Style.Color;
                    toolStyles[ToolKind.Text] = CloneStyle(canvas.Style);
                    UpdateSizeDisplay();
                    textDragOffset = new PointF(e.X - existing.Points[0].X, e.Y - existing.Points[0].Y);
                }
                else
                {
                    movingMarker = existing;
                    selectedMarker = existing;
                    markerView = existing.ViewMode;
                    UpdateViewButton();
                    markerDragOffset = new PointF(e.X - existing.Points[0].X, e.Y - existing.Points[0].Y);
                }
                drawing = true;
                return;
            }
            if (CurrentTool == ToolKind.Text) selectedText = null;
            if (CurrentTool is ToolKind.Person or ToolKind.Camera) selectedMarker = null;
        }
        drawing = true;
        start = e.Location;
        canvas.StartPoint = start;
        canvas.CurrentPoint = start;
        canvas.PreviewItem = new AnnotationItem
        {
            Tool = CurrentTool,
            ViewMode = markerView,
            Style = CloneStyle(canvas.Style),
            Points = new() { start }
        };
        if (CurrentTool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Wave or ToolKind.Eraser) canvas.Items.Add(canvas.PreviewItem);
    }
    private void OverlayMouseMove(object? s, MouseEventArgs e)
    {
        if (!drawing) return;
        if (movingText != null)
        {
            movingText.Points[0] = new PointF(e.X - textDragOffset.X, e.Y - textDragOffset.Y);
            canvas.Invalidate();
            return;
        }
        if (movingMarker != null)
        {
            var dx = e.X - markerDragOffset.X - movingMarker.Points[0].X;
            var dy = e.Y - markerDragOffset.Y - movingMarker.Points[0].Y;
            for (var i = 0; i < movingMarker.Points.Count; i++) movingMarker.Points[i] = new PointF(movingMarker.Points[i].X + dx, movingMarker.Points[i].Y + dy);
            canvas.Invalidate();
            return;
        }
        canvas.CurrentPoint = e.Location;
        if (CurrentTool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Wave or ToolKind.Eraser) canvas.Items[^1].Points.Add(e.Location);
        else canvas.PreviewItem!.Points = new() { start, e.Location };
        canvas.Invalidate();
    }
    private void OverlayMouseUp(object? s, MouseEventArgs e)
    {
        if (!drawing) return;
        drawing = false;
        if (movingText != null)
        {
            movingText = null;
            canvas.Invalidate();
            return;
        }
        if (movingMarker != null)
        {
            movingMarker = null;
            canvas.Invalidate();
            return;
        }
        if (CurrentTool is not (ToolKind.Pen or ToolKind.Highlighter or ToolKind.Wave or ToolKind.Eraser))
        {
            if (CurrentTool == ToolKind.Text)
            {
                string? text = null;
                using var d = new TextEntryForm();
                if (d.ShowDialog(this) == DialogResult.OK) text = d.Value;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    canvas.PreviewItem!.Text = text;
                    canvas.Items.Add(canvas.PreviewItem);
                }
            }
            else canvas.Items.Add(canvas.PreviewItem!);
        }
        canvas.PreviewItem = null;
        canvas.Invalidate();
    }
    private static AnnotationStyle CloneStyle(AnnotationStyle s) => new() { Color = s.Color, Width = s.Width, Alpha = s.Alpha, FontSize = s.FontSize };
    private static AnnotationStyle DefaultStyle(ToolKind tool) => tool switch
    {
        ToolKind.Highlighter => new AnnotationStyle { Color = "#FFEB3B", Width = 8, Alpha = 100 },
        ToolKind.Eraser => new AnnotationStyle { Color = "#FF00FF", Width = 8, Alpha = 255 },
        ToolKind.Text => new AnnotationStyle { Color = "#E53935", Width = 4, Alpha = 255, FontSize = 24 },
        ToolKind.Person => new AnnotationStyle { Color = "#E53935", Width = 4, Alpha = 255 },
        ToolKind.Camera => new AnnotationStyle { Color = "#1E88E5", Width = 4, Alpha = 255 },
        _ => new AnnotationStyle { Color = "#E53935", Width = 4, Alpha = 255 }
    };

    private void ToggleMarkerView()
    {
        if (CurrentTool is not (ToolKind.Person or ToolKind.Camera)) return;
        markerView = markerView == MarkerView.Top ? MarkerView.Elevation : MarkerView.Top;
        if (selectedMarker?.Tool == CurrentTool)
        {
            selectedMarker.ViewMode = markerView;
            canvas.Invalidate();
        }
        UpdateViewButton();
    }

    private void UpdateViewButton()
    {
        if (viewButton == null) return;
        viewButton.Text = markerView == MarkerView.Top ? "俯视 V" : "立面 V";
        viewButton.Enabled = CurrentTool is ToolKind.Person or ToolKind.Camera;
    }
    private void ChangeToolSize(int delta)
    {
        var next = CurrentTool == ToolKind.Text
            ? Math.Clamp((int)canvas.Style.FontSize + delta, 8, 120)
            : Math.Clamp(canvas.Style.Width + delta, 1, 50);
        if (CurrentTool == ToolKind.Text) canvas.Style.FontSize = next;
        else canvas.Style.Width = next;
        if (CurrentTool == ToolKind.Text && selectedText != null)
        {
            selectedText.Style.FontSize = next;
            canvas.Invalidate();
        }
        toolStyles[CurrentTool] = CloneStyle(canvas.Style);
        if (sizeControl != null) sizeControl.Value = next;
        UpdateSizeHint();
    }
    private void SetCurrentColor(string html)
    {
        canvas.Style.Color = html;
        toolStyles[CurrentTool] = CloneStyle(canvas.Style);
    }
    private void UpdateSizeDisplay()
    {
        if (sizeControl != null)
        {
            sizeControl.Maximum = CurrentTool == ToolKind.Text ? 120 : 50;
            var value = CurrentTool == ToolKind.Text ? (int)canvas.Style.FontSize : canvas.Style.Width;
            sizeControl.Value = Math.Clamp(value, (int)sizeControl.Minimum, (int)sizeControl.Maximum);
        }
        UpdateSizeHint();
    }
    private void UpdateSizeHint()
    {
        if (sizeHint == null) return;
        if (CurrentTool == ToolKind.Text)
        {
            sizeHint.Text = $"字号 {(int)canvas.Style.FontSize}  ↑↓±1 ←→±5";
            return;
        }
        var actual = CurrentTool == ToolKind.Eraser ? canvas.Style.Width * 4 : canvas.Style.Width;
        sizeHint.Text = CurrentTool == ToolKind.Eraser ? $"大小 {canvas.Style.Width}（擦除 {actual}）" : $"大小 {canvas.Style.Width}  ↑↓±1 ←→±5";
    }
    public void ReplaceItems(IEnumerable<AnnotationItem> items)
    {
        canvas.Items.Clear();
        canvas.Items.AddRange(items);
        selectedMarker = null;
        selectedText = null;
        canvas.Invalidate();
    }
    public void ResetAnnotations()
    {
        canvas.Items.Clear();
        canvas.PreviewItem = null;
        selectedText = null;
        selectedMarker = null;
        movingText = null;
        movingMarker = null;
        canvas.Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        if (m.Msg == WmNcHitTest)
        {
            var screenPoint = Cursor.Position;
            var clientPoint = PointToClient(screenPoint);
            if (!toolbar.Visible || !toolbar.Bounds.Contains(clientPoint))
            {
                m.Result = (IntPtr)HtTransparent;
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && inputLayer != null)
        {
            inputLayer.Close();
            inputLayer.Dispose();
            inputLayer = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class InputCaptureForm : Form
{
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExNoActivate = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate;
            return cp;
        }
    }

    public InputCaptureForm(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.004;
        KeyPreview = true;
        Cursor = Cursors.Cross;
    }
}
