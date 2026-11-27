using ScreenAnnotator.Models;

namespace ScreenAnnotator;

internal sealed record CaptureMode(string Name, CaptureRatio? Ratio, string? Description = null)
{
    public static readonly CaptureMode Free = new("自由选择", null);
    public override string ToString() => Description is null ? Name : $"{Name}  {Description}";
}

internal sealed class CaptureSelector : Form
{
    private static readonly CaptureMode[] Presets =
    {
        CaptureMode.Free,
        new("16:9", new CaptureRatio(16, 9), "横屏"),
        new("9:16", new CaptureRatio(9, 16), "竖屏"),
        new("4:3", new CaptureRatio(4, 3)),
        new("3:4", new CaptureRatio(3, 4)),
        new("3:2", new CaptureRatio(3, 2)),
        new("2:3", new CaptureRatio(2, 3)),
        new("1:1", new CaptureRatio(1, 1), "方形"),
        new("21:9", new CaptureRatio(21, 9), "宽幅"),
        new("2.39:1", new CaptureRatio(239, 100), "电影宽幅")
    };

    private readonly Bitmap frame;
    private readonly FlowLayoutPanel ratioBar;
    private readonly ComboBox modes;
    private readonly Button confirmButton;
    private readonly Button cancelButton;
    private CaptureMode? customMode;
    private Point start;
    private Point end;
    private bool hasAnchor;
    private bool selecting;

    public Rectangle Selection { get; private set; }
    public CaptureMode Mode => (CaptureMode)modes.SelectedItem!;

    public CaptureSelector(Rectangle bounds, Bitmap frame, CaptureMode mode)
    {
        this.frame = frame;
        Text = "屏幕笔 · 截图";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        Font = SystemFonts.MessageBoxFont;

        ratioBar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(ScalePixels(9), ScalePixels(6), ScalePixels(9), ScalePixels(6)),
            BackColor = Color.FromArgb(32, 36, 45),
            ForeColor = Color.White,
            Cursor = Cursors.Default
        };
        var label = new Label
        {
            Text = "截图比例",
            AutoSize = true,
            Margin = new Padding(ScalePixels(3), ScalePixels(8), ScalePixels(6), 0)
        };
        modes = new ComboBox
        {
            AccessibleName = "截图比例",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = ScalePixels(174),
            Margin = new Padding(0, ScalePixels(4), ScalePixels(6), 0)
        };
        modes.Items.AddRange(Presets);
        if (!Presets.Contains(mode))
        {
            customMode = mode;
            modes.Items.Add(mode);
        }
        modes.SelectedItem = mode;
        modes.SelectedIndexChanged += (_, _) => UpdateSelection();
        var custom = new Button
        {
            Text = "自定义…",
            Width = ScalePixels(84),
            Height = ScalePixels(30),
            FlatStyle = FlatStyle.Flat,
            Margin = Padding.Empty,
            Cursor = Cursors.Hand
        };
        custom.Click += (_, _) => SetCustomRatio();
        ratioBar.Controls.AddRange(new Control[] { label, modes, custom });
        Controls.Add(ratioBar);

        cancelButton = ActionButton("×", "取消截图", Color.FromArgb(175, 48, 62), () => DialogResult = DialogResult.Cancel);
        confirmButton = ActionButton("✓", "复制截图", Color.FromArgb(24, 135, 78), Confirm);
        Controls.AddRange(new Control[] { cancelButton, confirmButton });
        ratioBar.SizeChanged += (_, _) => PositionRatioBar();
        Shown += (_, _) => PositionRatioBar();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!modes.DroppedDown && keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        if (!modes.DroppedDown && keyData == Keys.Enter && !Selection.IsEmpty)
        {
            Confirm();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        start = end = e.Location;
        selecting = hasAnchor = true;
        Capture = true;
        ratioBar.Visible = false;
        UpdateSelection();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!selecting) return;
        end = e.Location;
        UpdateSelection();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!selecting || e.Button != MouseButtons.Left) return;
        end = e.Location;
        selecting = false;
        Capture = false;
        ratioBar.Visible = true;
        UpdateSelection();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!selecting || Capture) return;
        selecting = false;
        ratioBar.Visible = true;
        UpdateSelection();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.DrawImageUnscaled(frame, Point.Empty);
        using var shade = new SolidBrush(Color.FromArgb(95, Color.Black));
        using var outside = new Region(ClientRectangle);
        if (!Selection.IsEmpty) outside.Exclude(Selection);
        e.Graphics.FillRegion(shade, outside);
        if (Selection.IsEmpty) return;

        using var border = new Pen(Color.FromArgb(58, 221, 168), ScalePixels(2));
        e.Graphics.DrawRectangle(border, Selection.X, Selection.Y, Selection.Width - 1, Selection.Height - 1);
        var sizeText = $"{Selection.Width} × {Selection.Height} px";
        if (Mode.Ratio != null) sizeText += $"   |   {Mode.Name}";

        var padding = ScalePixels(5);
        var size = TextRenderer.MeasureText(sizeText, Font);
        var x = Math.Clamp(Selection.Left, 0, Math.Max(0, ClientSize.Width - size.Width - padding * 2));
        var y = Selection.Top - size.Height - padding * 2;
        if (y < 0) y = Selection.Top;
        var bubble = new Rectangle(x, y, size.Width + padding * 2, size.Height + padding * 2);
        if (ratioBar.Visible && bubble.IntersectsWith(ratioBar.Bounds))
            bubble.Y = ratioBar.Bottom + padding;

        using var labelBackground = new SolidBrush(Color.FromArgb(32, 36, 45));
        e.Graphics.FillRectangle(labelBackground, bubble);
        TextRenderer.DrawText(e.Graphics, sizeText, Font, new Point(bubble.X + padding, bubble.Y + padding), Color.White);
    }

    private void SetCustomRatio()
    {
        using var dialog = new Form
        {
            Text = "自定义截图比例",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ShowInTaskbar = false,
            MinimizeBox = false,
            MaximizeBox = false,
            Font = Font
        };
        var contents = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(ScalePixels(15))
        };
        var hint = new Label { Text = "输入宽:高，例如 5:4、2.35:1 或 2560:1440", AutoSize = true };
        var input = new TextBox
        {
            Text = customMode?.Name ?? (Mode.Ratio != null ? Mode.Name : "16:9"),
            AccessibleName = "宽高比",
            Width = ScalePixels(250)
        };
        var message = new Label { Text = " ", ForeColor = Color.Firebrick, AutoSize = true };
        var apply = new Button { Text = "应用比例", AutoSize = true };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange(new Control[] { apply, cancel });
        contents.Controls.AddRange(new Control[] { hint, input, message, buttons });
        dialog.Controls.Add(contents);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = input;
        dialog.Shown += (_, _) => input.SelectAll();

        CaptureRatio customRatio = default;
        apply.Click += (_, _) =>
        {
            if (!CaptureRatio.TryParse(input.Text, out customRatio))
            {
                message.Text = "请输入有效的宽:高，最多三位小数。";
                input.Focus();
                return;
            }
            var step = customRatio.PixelStep;
            if (step.Width > frame.Width || step.Height > frame.Height)
            {
                message.Text = $"该比例最小为 {step.Width} × {step.Height} px，超出屏幕。";
                input.Focus();
                return;
            }
            dialog.DialogResult = DialogResult.OK;
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var newMode = new CaptureMode(input.Text.Trim().Replace('：', ':'), customRatio, "自定义");
        if (newMode != customMode)
        {
            var previousCustomMode = customMode;
            customMode = newMode;
            modes.Items.Add(newMode);
            modes.SelectedItem = newMode;
            if (previousCustomMode != null) modes.Items.Remove(previousCustomMode);
        }
        modes.SelectedItem = customMode;
    }

    private void Confirm()
    {
        if (!selecting && Selection.Width > 0 && Selection.Height > 0)
            DialogResult = DialogResult.OK;
    }

    private void UpdateSelection()
    {
        if (!hasAnchor) return;
        Selection = CaptureRatio.Select(start, end, frame.Size, (modes.SelectedItem as CaptureMode)?.Ratio);
        PositionActionButtons();
        Invalidate();
    }

    private void PositionActionButtons()
    {
        confirmButton.Visible = cancelButton.Visible = !selecting && !Selection.IsEmpty;
        if (!confirmButton.Visible) return;

        var gap = ScalePixels(6);
        var width = cancelButton.Width + gap + confirmButton.Width;
        var left = Math.Clamp(Selection.Right - width, 0, Math.Max(0, ClientSize.Width - width));
        var top = Selection.Bottom + gap;
        if (top + confirmButton.Height > ClientSize.Height)
            top = Math.Max(0, Selection.Bottom - confirmButton.Height - gap);
        if (new Rectangle(left, top, width, confirmButton.Height).IntersectsWith(ratioBar.Bounds))
            top = ratioBar.Bottom + gap;
        cancelButton.Location = new Point(left, top);
        confirmButton.Location = new Point(left + cancelButton.Width + gap, top);
    }

    private void PositionRatioBar() =>
        ratioBar.Location = new Point(Math.Max(0, (ClientSize.Width - ratioBar.Width) / 2), ScalePixels(12));

    private int ScalePixels(int pixels) => LogicalToDeviceUnits(pixels);

    private Button ActionButton(string text, string description, Color color, Action click)
    {
        var button = new Button
        {
            Text = text,
            AccessibleName = description,
            Width = ScalePixels(48),
            Height = ScalePixels(37),
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Visible = false
        };
        button.Click += (_, _) => click();
        return button;
    }
}
