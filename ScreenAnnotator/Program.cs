using System;
using System.Threading;
using System.Windows.Forms;

namespace ScreenAnnotator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "ScreenAnnotator.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("屏幕笔已经在运行中。请从任务栏托盘使用现有实例。", "屏幕笔", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
