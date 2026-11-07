using System.Runtime.InteropServices;

namespace ScreenAnnotator.Services;

public sealed class HotkeyService : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private readonly Dictionary<int, Action> actions = new();
    private int nextId = 100;
    public HotkeyService() { CreateHandle(new CreateParams()); }
    public bool Register(Keys key, Action action)
    {
        uint mods = 0;
        if ((key & Keys.Alt) == Keys.Alt) mods |= 0x0001;
        if ((key & Keys.Control) == Keys.Control) mods |= 0x0002;
        if ((key & Keys.Shift) == Keys.Shift) mods |= 0x0004;
        var vk = (uint)(key & Keys.KeyCode); var id = nextId++;
        if (!RegisterHotKey(Handle, id, mods, vk)) return false;
        actions[id] = action; return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && actions.TryGetValue(m.WParam.ToInt32(), out var action)) action();
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        foreach (var id in actions.Keys) UnregisterHotKey(Handle, id);
        actions.Clear(); DestroyHandle(); GC.SuppressFinalize(this);
    }
}
