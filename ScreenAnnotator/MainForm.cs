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
    private readonly ContextMenuStrip trayMenu = new();
    private readonly HotkeyService hotkeys = new();
    private OverlayForm? overlay;
    private readonly Stack<AnnotationItem> undo = new();
    private readonly Stack<AnnotationItem> redo = new();
    private string? currentProject;
    private IntPtr underlyingWindow;
    private bool captureInProgress;
    private bool exitRequested;
    private bool resourcesDisposed;
    private CaptureMode captureMode = CaptureMode.Free;

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
        trayMenu.Items.Add("开始屏幕笔 (F2)", null, (_, _) => ToggleOverlay());
        trayMenu.Items.Add("重置屏幕标注 (F1)", null, (_, _) => ResetOverlay());
        trayMenu.Items.Add("使用说明", null, (_, _) => new HelpForm().Show(this));
        trayMenu.Items.Add("打开主窗口", null, (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        });
        trayMenu.Items.Add("退出", null, (_, _) => ExitFromTray());
        tray.Text = "屏幕笔";
        tray.Icon = Icon;
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => ToggleOverlay();
        tray.Visible = true;
    }

    private void ExitFromTray()
    {
        if (exitRequested) return;
        exitRequested = true;
        tray.Visible = false;
        Application.ExitThread();
    }
    private void RegisterHotkeys()
    {
        hotkeys.Register(settings.ToggleOverlay, ToggleOverlay);
        hotkeys.Register(Keys.F1, ResetOverlay);
        hotkeys.Register(Keys.F3, BeginRegionCapture);
    }
    private void ToggleOverlay()
    {
        if (exitRequested || captureInProgress) return;
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
        if (exitRequested || overlay == null || captureInProgress) return;
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
        if (exitRequested || overlay == null || !overlay.IsEditing || captureInProgress) return;
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
        if (overlay is not { } activeOverlay) return;
        var bounds = Screen.FromPoint(Cursor.Position).Bounds;
        var toolbarWasVisible = activeOverlay.ToolbarVisible;
        var copied = false;
        try
        {
            activeOverlay.PrepareForCapture();
            using var frame = ScreenCaptureService.Capture(bounds);
            using var selector = new CaptureSelector(bounds, frame, captureMode);
            var result = selector.ShowDialog(activeOverlay);
            captureMode = selector.Mode;
            if (exitRequested || result != DialogResult.OK) return;

            using var bitmap = frame.Clone(selector.Selection, frame.PixelFormat);
            Clipboard.SetImage(bitmap);
            copied = true;
            activeOverlay.SetEditing(false);
            RestoreUnderlyingWindow();
        }
        catch (Exception ex)
        {
            if (!exitRequested)
                MessageBox.Show(activeOverlay, $"截图失败：{ex.Message}", "屏幕笔", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!copied && !exitRequested)
            {
                activeOverlay.SetEditing(true);
                activeOverlay.ToolbarVisible = toolbarWasVisible;
            }
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
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            exitRequested = true;
            tray.Visible = false;
            tray.Dispose();
            trayMenu.Dispose();
            hotkeys.Dispose();
            overlay?.Dispose();
            overlay = null;
        }
        base.Dispose(disposing);
    }
}
