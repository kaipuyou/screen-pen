using ScreenAnnotator.Models;
using ScreenAnnotator.Services;
using System.Runtime.InteropServices;

namespace ScreenAnnotator;

public sealed class MainForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    private const int SwRestore = 9;
    private readonly AppSettings settings = SettingsService.Load();
    private readonly NotifyIcon tray = new();
    private readonly HotkeyService hotkeys = new();
    private OverlayForm? overlay;
    private readonly Stack<AnnotationItem> undo = new();
    private readonly Stack<AnnotationItem> redo = new();
    private string? currentProject;
    private IntPtr underlyingWindow;
    private bool captureInProgress;

    public MainForm()
    {
        Text = "屏幕笔"; Width = 560; Height = 360; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi(); BuildTray(); RegisterHotkeys();
        Shown += (_, _) => { if (settings.ShowWelcome) { using var h = new HelpForm(); h.ShowDialog(this); settings.ShowWelcome = false; SettingsService.Save(settings); } };
    }

    private void BuildUi()
    {
        var title = new Label { Text = "屏幕笔", Font = new Font("Microsoft YaHei UI", 24, FontStyle.Bold), AutoSize = true, Left = 28, Top = 24 };
        var sub = new Label { Text = "在任何项目页面上直接标注，再框选截图", AutoSize = true, Left = 32, Top = 72, ForeColor = Color.DimGray };
        var start = new Button { Text = "开始屏幕笔  (F2)", Width = 220, Height = 44, Left = 32, Top = 120 }; start.Click += (_, _) => ToggleOverlay();
        var help = new Button { Text = "使用提示与说明书", Width = 220, Height = 44, Left = 270, Top = 120 }; help.Click += (_, _) => new HelpForm().Show(this);
        var open = new Button { Text = "打开项目", Width = 140, Height = 38, Left = 32, Top = 190 }; open.Click += (_, _) => LoadProject();
        var save = new Button { Text = "保存当前项目", Width = 140, Height = 38, Left = 190, Top = 190 }; save.Click += (_, _) => SaveProject();
        var copyInfo = new Button { Text = "截图仅复制", Width = 140, Height = 38, Left = 348, Top = 190, Enabled = false };
        var note = new Label { Text = "提示：按 F3 框选截图，点击 ✓ 只复制到剪贴板并结束屏幕笔；再按 F2 可恢复原标注。", AutoSize = false, Width = 480, Height = 50, Left = 32, Top = 255, ForeColor = Color.DarkSlateGray };
        Controls.AddRange(new Control[] { title, sub, start, help, open, save, copyInfo, note });
    }
    private void BuildTray()
    {
        var menu = new ContextMenuStrip(); menu.Items.Add("开始屏幕笔 (F2)", null, (_, _) => ToggleOverlay()); menu.Items.Add("重置屏幕标注 (F1)", null, (_, _) => ResetOverlay()); menu.Items.Add("使用说明", null, (_, _) => new HelpForm().Show()); menu.Items.Add("打开主窗口", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); }); menu.Items.Add("退出", null, (_, _) => Application.Exit());
        tray.Text = "屏幕笔"; tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; tray.ContextMenuStrip = menu; tray.Visible = true; tray.DoubleClick += (_, _) => ToggleOverlay();
    }
    private void RegisterHotkeys()
    {
        hotkeys.Register(settings.ToggleOverlay, ToggleOverlay);
        hotkeys.Register(Keys.F1, ResetOverlay);
        hotkeys.Register(Keys.F3, BeginRegionCapture);
    }
    private void ToggleOverlay()
    {
        if (overlay != null)
        {
            // F2 是“开始使用”。已在编辑时不改变现场，避免误触导致结束。
            if (overlay.IsEditing)
            {
                return;
            }
            else
            {
                RememberUnderlyingWindow();
                overlay.SetEditing(true);
                ShowStatus("已恢复编辑模式，可继续补画或修改标注");
            }
            return;
        }
        RememberUnderlyingWindow();
        var screen = Screen.FromPoint(Cursor.Position); overlay = new OverlayForm(this, screen); overlay.Show(); Hide();
    }

    private void RememberUnderlyingWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero && hwnd != Handle && hwnd != overlay?.Handle)
            underlyingWindow = hwnd;
    }

    private void RestoreUnderlyingWindow()
    {
        if (underlyingWindow == IntPtr.Zero || !IsWindow(underlyingWindow)) return;
        ShowWindow(underlyingWindow, SwRestore);
        SetForegroundWindow(underlyingWindow);
    }

    public void ResetOverlay()
    {
        if (overlay == null) return;
        var wasEditing = overlay.IsEditing;
        overlay.ResetAnnotations();
        if (wasEditing) overlay.SetEditing(true);
        ShowStatus(wasEditing ? "已重置屏幕标注，当前画面为空白" : "已重置屏幕标注；按 F2 可重新开始");
    }
    public void ExitOverlay()
    {
        if (overlay == null) return; overlay.Close(); overlay.Dispose(); overlay = null; Show(); Activate();
    }

    public void PauseOverlay()
    {
        if (overlay == null) return;
        overlay.SetEditing(false);
    }
    public void BeginRegionCapture()
    {
        // 截图按微信习惯只在屏幕笔正在接管时响应，结束后不会误触发。
        if (overlay == null || !overlay.IsEditing || captureInProgress) return;
        captureInProgress = true;
        try
        {
            BeginRegionCaptureCore();
        }
        finally
        {
            captureInProgress = false;
        }
    }

    private void BeginRegionCaptureCore()
    {
        if (overlay == null) return;
        var screen = Screen.FromPoint(Cursor.Position); var bounds = screen.Bounds;
        using var selector = new CaptureSelector(bounds);
        overlay.ToolbarVisible = false;
        var result = selector.ShowDialog(overlay);
        if (result == DialogResult.Cancel || selector.Action == CaptureAction.Cancel || selector.Selection.Width <= 0 || selector.Selection.Height <= 0)
        {
            overlay.SetEditing(true);
            return;
        }
        // 对话框已完成，先彻底隐藏截图选择层，再读取屏幕合成画面。
        selector.Hide();
        var captureRect = new Rectangle(bounds.Left + selector.Selection.Left, bounds.Top + selector.Selection.Top, selector.Selection.Width, selector.Selection.Height);
        using var bitmap = ScreenCaptureService.Capture(captureRect);
        try
        {
            Clipboard.SetImage(new Bitmap(bitmap));
            overlay.SetEditing(false);
            RestoreUnderlyingWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"复制截图失败：{ex.Message}", "屏幕笔", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    public void SaveProject()
    {
        if (overlay == null) return;
        if (string.IsNullOrWhiteSpace(currentProject)) { using var d = new SaveFileDialog { Filter = "屏幕笔项目 (*.json)|*.json", FileName = $"标注_{DateTime.Now:yyyyMMdd_HHmmss}.json" }; if (d.ShowDialog(this) != DialogResult.OK) return; currentProject = d.FileName; }
        var screen = Screen.FromPoint(Cursor.Position); ProjectFileService.Save(currentProject, new AnnotationProject { ScreenDevice = screen.DeviceName, ScreenWidth = screen.Bounds.Width, ScreenHeight = screen.Bounds.Height, Items = overlay.Items.ToList() });
        tray.ShowBalloonTip(1500, "屏幕笔", "项目已保存", ToolTipIcon.Info);
    }
    private void LoadProject()
    {
        using var d = new OpenFileDialog { Filter = "屏幕笔项目 (*.json)|*.json" }; if (d.ShowDialog(this) != DialogResult.OK) return; var project = ProjectFileService.Load(d.FileName); currentProject = d.FileName;
        if (overlay == null) { var screen = Screen.FromPoint(Cursor.Position); overlay = new OverlayForm(this, screen); overlay.Show(); Hide(); }
        overlay!.ReplaceItems(project.Items);
    }
    public void Undo() { if (overlay == null) return; var item = overlay.RemoveLastItem(); if (item != null) redo.Push(item); }
    public void Redo() { if (overlay == null || redo.Count == 0) return; overlay.AddItem(redo.Pop()); }
    public void ShowStatus(string message) => tray.ShowBalloonTip(1800, "屏幕笔", message, ToolTipIcon.Info);
    protected override void OnFormClosing(FormClosingEventArgs e) { hotkeys.Dispose(); tray.Visible = false; tray.Dispose(); overlay?.Dispose(); base.OnFormClosing(e); }
}

internal sealed class CaptureSelector : Form
{
    private readonly Button confirmButton;
    private readonly Button cancelButton;
    private Point start;
    private bool selecting;
    public Rectangle Selection { get; private set; }
    public CaptureAction Action { get; private set; } = CaptureAction.Cancel;

    public CaptureSelector(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = .32;
        Cursor = Cursors.Cross;
        KeyPreview = true;

        cancelButton = new Button { Text = "×", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold), Width = 46, Height = 40, BackColor = Color.FromArgb(210, 55, 55), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Visible = false };
        confirmButton = new Button { Text = "✓", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold), Width = 46, Height = 40, BackColor = Color.FromArgb(35, 160, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Visible = false };
        confirmButton.Click += (_, _) => Complete(CaptureAction.Copy);
        cancelButton.Click += (_, _) => Complete(CaptureAction.Cancel);
        Controls.Add(cancelButton);
        Controls.Add(confirmButton);

        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            selecting = true;
            start = e.Location;
            Selection = Rectangle.Empty;
            confirmButton.Visible = false;
            cancelButton.Visible = false;
            Invalidate();
        };
        MouseMove += (_, e) =>
        {
            if (!selecting) return;
            Selection = Normalize(start, e.Location);
            Invalidate();
        };
        MouseUp += (_, e) =>
        {
            if (!selecting || e.Button != MouseButtons.Left) return;
            selecting = false;
            Selection = Normalize(start, e.Location);
            PositionActionButtons();
            Invalidate();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Complete(CaptureAction.Cancel);
            else if (e.KeyCode == Keys.Enter) Complete(CaptureAction.Copy);
        };
    }

    private void Complete(CaptureAction action)
    {
        if (action != CaptureAction.Cancel && (Selection.Width <= 0 || Selection.Height <= 0)) return;
        Action = action;
        DialogResult = action == CaptureAction.Cancel ? DialogResult.Cancel : DialogResult.OK;
    }

    private void PositionActionButtons()
    {
        if (Selection.Width < 2 || Selection.Height < 2) return;
        var totalWidth = confirmButton.Width + cancelButton.Width + 6;
        var left = Math.Clamp(Selection.Right - totalWidth, 4, Math.Max(4, ClientSize.Width - totalWidth - 4));
        var below = Selection.Bottom + 6;
        var top = below + confirmButton.Height <= ClientSize.Height ? below : Math.Max(4, Selection.Bottom - confirmButton.Height - 6);
        cancelButton.Location = new Point(left, top);
        confirmButton.Location = new Point(left + cancelButton.Width + 6, top);
        confirmButton.Visible = true;
        cancelButton.Visible = true;
        confirmButton.BringToFront();
        cancelButton.BringToFront();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Selection.Width <= 0 || Selection.Height <= 0) return;
        using var pen = new Pen(Color.FromArgb(255, 75, 75), 3);
        e.Graphics.DrawRectangle(pen, Selection);
        using var brush = new SolidBrush(Color.White);
        using var font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        e.Graphics.DrawString($"{Selection.Width} × {Selection.Height}   ×取消   ✓复制", font, brush, Selection.Left + 4, Math.Max(2, Selection.Top - 24));
    }

    private static Rectangle Normalize(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

internal enum CaptureAction
{
    Cancel,
    Copy
}
