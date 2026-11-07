using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenAnnotatorInstaller;

internal static class Program
{
    private const string ProductName = "屏幕笔";
    private const string ExeName = "ScreenAnnotator.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenAnnotator";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Uninstall();
            return;
        }

        using var form = new InstallerForm();
        Application.Run(form);
    }

    private sealed class InstallerForm : Form
    {
        private readonly Button installButton = new();
        private readonly CheckBox launchAfterInstall = new();
        private readonly ProgressBar progress = new();

        public InstallerForm()
        {
            Text = "安装屏幕笔";
            Width = 520;
            Height = 330;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

            var title = new Label { Text = "屏幕笔", Font = new Font("Microsoft YaHei UI", 26, FontStyle.Bold), AutoSize = true, Left = 34, Top = 28 };
            var subtitle = new Label { Text = "透明屏幕标注与微信式截图工具", Font = new Font("Microsoft YaHei UI", 11), AutoSize = true, Left = 37, Top = 84, ForeColor = Color.DimGray };
            var info = new Label
            {
                Text = "安装后自动创建桌面和开始菜单快捷方式。\n目标电脑无需安装 Visual Studio 或 .NET。\n\n快捷键：F2 开始/恢复  ·  F3 截图  ·  F1 清空\n标识：I 人形  ·  C 相机  ·  V 俯视/立面",
                Font = new Font("Microsoft YaHei UI", 10),
                AutoSize = true,
                Left = 37,
                Top = 126
            };
            launchAfterInstall.Text = "安装完成后运行屏幕笔";
            launchAfterInstall.Checked = true;
            launchAfterInstall.AutoSize = true;
            launchAfterInstall.Left = 37;
            launchAfterInstall.Top = 215;
            progress.Left = 37;
            progress.Top = 248;
            progress.Width = 285;
            progress.Height = 18;
            progress.Visible = false;
            installButton.Text = "立即安装";
            installButton.Width = 126;
            installButton.Height = 44;
            installButton.Left = 345;
            installButton.Top = 220;
            installButton.Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
            installButton.Click += async (_, _) => await InstallAsync();
            Controls.AddRange(new Control[] { title, subtitle, info, launchAfterInstall, progress, installButton });
        }

        private async Task InstallAsync()
        {
            installButton.Enabled = false;
            progress.Visible = true;
            progress.Style = ProgressBarStyle.Marquee;
            try
            {
                var installedExe = await Task.Run(InstallFiles);
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = 100;
                MessageBox.Show(this, "屏幕笔安装成功！\n\n桌面快捷方式已经创建。", "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (launchAfterInstall.Checked) Process.Start(new ProcessStartInfo(installedExe) { UseShellExecute = true });
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"安装失败：{ex.Message}", "屏幕笔", MessageBoxButtons.OK, MessageBoxIcon.Error);
                installButton.Enabled = true;
                progress.Visible = false;
            }
        }
    }

    private static string InstallFiles()
    {
        var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenAnnotator");
        Directory.CreateDirectory(installDir);
        var exePath = Path.Combine(installDir, ExeName);
        ExtractResource("Payload.ScreenAnnotator.exe", exePath);
        var iconPath = Path.Combine(installDir, "ScreenAnnotator.ico");
        ExtractResource("Payload.ScreenAnnotator.ico", iconPath);

        var uninstallerPath = Path.Combine(installDir, "Uninstall.exe");
        File.Copy(Application.ExecutablePath, uninstallerPath, true);

        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "屏幕笔.lnk"), exePath, iconPath);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var menuDir = Path.Combine(programs, ProductName);
        Directory.CreateDirectory(menuDir);
        CreateShortcut(Path.Combine(menuDir, "屏幕笔.lnk"), exePath, iconPath);
        CreateShortcut(Path.Combine(menuDir, "卸载屏幕笔.lnk"), uninstallerPath, iconPath, "/uninstall");

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key?.SetValue("DisplayName", ProductName);
        key?.SetValue("DisplayVersion", "1.0.0");
        key?.SetValue("Publisher", "知溯工具");
        key?.SetValue("DisplayIcon", exePath);
        key?.SetValue("InstallLocation", installDir);
        key?.SetValue("UninstallString", $"\"{uninstallerPath}\" /uninstall");
        key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        return exePath;
    }

    private static void ExtractResource(string name, string outputPath)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException($"安装资源缺失：{name}");
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyTo(output);
    }

    private static void Uninstall()
    {
        if (MessageBox.Show("确定要卸载屏幕笔吗？", "卸载屏幕笔", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            foreach (var process in Process.GetProcessesByName("ScreenAnnotator"))
            {
                try { process.Kill(true); process.WaitForExit(3000); } catch { }
            }
            var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "屏幕笔.lnk");
            if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut);
            var menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName);
            if (Directory.Exists(menuDir)) Directory.Delete(menuDir, true);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenAnnotator");
            var cleanup = Path.Combine(Path.GetTempPath(), $"ScreenAnnotatorCleanup_{Guid.NewGuid():N}.cmd");
            File.WriteAllText(cleanup, $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\nrmdir /s /q \"{installDir}\"\r\ndel /q \"%~f0\"\r\n", Encoding.ASCII);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cleanup}\"") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            MessageBox.Show("屏幕笔已卸载。", "屏幕笔", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"卸载失败：{ex.Message}", "屏幕笔", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string iconPath, string arguments = "")
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetArguments(arguments);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
        link.SetIconLocation(iconPath, 0);
        link.SetDescription(ProductName);
        ((IPersistFile)link).Save(shortcutPath, true);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
