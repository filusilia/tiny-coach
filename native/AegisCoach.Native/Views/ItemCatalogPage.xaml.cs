using AegisCoach.Native.Models;
using AegisCoach.Native.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AegisCoach.Native.Views;

/// <summary>
/// 物品资料库与悬浮交互卡片页：分组网格 + 窗口内 Popup 详情卡。
/// 数据加载由 MainWindow 在设置就绪后触发（遵守首次启动门控）。
/// </summary>
public sealed partial class ItemCatalogPage : Page
{
    /// <summary>悬浮详情卡宽高估算（用于把卡片夹取到窗口内；卡片实际内容可能更矮，允许底部少留白）。</summary>
    private const double DetailCardWidth = 372 + 8;
    private const double DetailCardEstimatedHeight = 320;

    private DispatcherQueueTimer? _popupCloseTimer;

    public ItemCatalogViewModel ViewModel { get; } = new();

    public ItemCatalogPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    /// <summary>由 MainWindow 在设置保存成功后调用（force=true 重新联网拉取）。</summary>
    public Task LoadAsync(bool force) => ViewModel.LoadCommand.ExecuteAsync(force);

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        DetailPopup.IsOpen = false;
        ViewModel.Cancel();
    }

    /// <summary>悬浮进入：补齐官方详情并打开窗口内 Popup（位置跟随指针，夹取在窗口范围内）。</summary>
    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogItem item }) return;
        DetailPopupContent.Content = item;
        _ = ViewModel.EnsureDetailsLoadedAsync(item);
        PositionDetailPopup(e.GetCurrentPoint(RootGrid).Position);
        DetailPopup.IsOpen = true;
    }

    /// <summary>移出物品格：延迟关闭，允许鼠标移入详情卡继续查看。</summary>
    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e) => SchedulePopupClose();

    private void DetailPopup_PointerEntered(object sender, PointerRoutedEventArgs e) => _popupCloseTimer?.Stop();

    private void DetailPopup_PointerExited(object sender, PointerRoutedEventArgs e) => SchedulePopupClose();

    /// <summary>把详情卡定位到指针附近，并夹取到窗口范围内。
    /// WinUI Popup 的 HorizontalOffset/VerticalOffset 是窗口客户区坐标，而指针坐标相对 RootGrid
    /// （NavigationView 内容区，左侧可能被 Pane 偏移），因此先用 TransformToVisual 换算成窗口坐标。</summary>
    private void PositionDetailPopup(Windows.Foundation.Point pointer)
    {
        var origin = RootGrid.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var maxX = origin.X + RootGrid.ActualWidth - DetailCardWidth - 6;
        var maxY = origin.Y + RootGrid.ActualHeight - DetailCardEstimatedHeight - 6;

        var offsetX = origin.X + pointer.X + 14;
        var offsetY = origin.Y + pointer.Y + 14;
        if (offsetX > maxX) offsetX = Math.Max(origin.X, origin.X + pointer.X - DetailCardWidth - 14);
        if (offsetY > maxY) offsetY = Math.Max(origin.Y, origin.Y + pointer.Y - DetailCardEstimatedHeight - 14);

        DetailPopup.HorizontalOffset = Math.Clamp(offsetX, origin.X, maxX);
        DetailPopup.VerticalOffset = Math.Clamp(offsetY, origin.Y, maxY);
    }

    private void SchedulePopupClose()
    {
        _popupCloseTimer ??= CreatePopupCloseTimer();
        _popupCloseTimer.Stop();
        _popupCloseTimer.Start();
    }

    private DispatcherQueueTimer CreatePopupCloseTimer()
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(120);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DetailPopup.IsOpen = false;
        };
        return timer;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.ApplyFilterNow();
    }
}
