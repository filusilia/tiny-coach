namespace AegisCoach.Launcher;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        // 优先在 lib/ 目录下寻找主程序组件，同时兼容 app/
        var candidates = new[]
        {
            Path.Combine(baseDir, "lib", "神盾教练.exe"),
            Path.Combine(baseDir, "app", "神盾教练.exe")
        };

        var targetExe = candidates.FirstOrDefault(File.Exists);
        if (targetExe is null)
        {
            MessageBox(IntPtr.Zero, "未能在 lib 文件夹内找到主程序组件 (神盾教练.exe)。\n请确保解压了完整便携版程序包，且勿移动或删除 lib 文件夹。", "神盾教练 便携启动器", 0x10);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = targetExe,
            WorkingDirectory = Path.GetDirectoryName(targetExe)!,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, $"启动神盾教练核心组件失败：\n{ex.Message}", "神盾教练 便携启动器", 0x10);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
