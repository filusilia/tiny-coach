using Microsoft.UI.Xaml;

namespace AegisCoach.Native;

public partial class App : Application
{
    private Window? _window;

    /// <summary>主窗口实例（页面用它读取窗口屏幕位置，把悬浮卡夹取到窗口内）。</summary>
    public Window? MainWindow => _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
