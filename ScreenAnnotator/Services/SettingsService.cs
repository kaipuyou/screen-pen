using System.Text.Json;

namespace ScreenAnnotator.Services;

public sealed class AppSettings
{
    public Keys ToggleOverlay { get; set; } = Keys.F2;
    public Keys Screenshot { get; set; } = Keys.F3;
    public bool ShowWelcome { get; set; } = true;
}

public static class SettingsService
{
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenAnnotator", "settings.json");
    public static AppSettings Load()
    {
        try
        {
            var settings = File.Exists(PathName) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathName)) ?? new() : new();
            // 旧版本使用 Ctrl+Shift+S，容易被其他截图软件抢占；迁移到独占的 F3。
            if (settings.Screenshot == (Keys.Control | Keys.Shift | Keys.S)) settings.Screenshot = Keys.F3;
            return settings;
        }
        catch { return new(); }
    }
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
