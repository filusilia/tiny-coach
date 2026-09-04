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
        _popupCloseTimer?.Stop();
        DetailPopupContent.Content = item;
        _ = ViewModel.EnsureDetailsLoadedAsync(item);
        PositionDetailPopup(e.GetCurrentPoint(RootGrid).Position);
        DetailPopup.IsOpen = true;
    }

    /// <summary>鼠标在物品格内移动时更新悬浮卡位置，实现平滑跟随与边界限制。</summary>
    private void Tile_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (DetailPopup.IsOpen)
        {
            PositionDetailPopup(e.GetCurrentPoint(RootGrid).Position);
        }
    }

    /// <summary>移出物品格：延迟关闭，允许鼠标移入详情卡继续查看。</summary>
    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e) => SchedulePopupClose();

    private void DetailPopup_PointerEntered(object sender, PointerRoutedEventArgs e) => _popupCloseTimer?.Stop();

    private void DetailPopup_PointerExited(object sender, PointerRoutedEventArgs e) => SchedulePopupClose();

    /// <summary>把详情卡定位到指针附近，并夹取到窗口可视区域内。</summary>
    private void PositionDetailPopup(Windows.Foundation.Point pointer)
    {
        var viewWidth = RootGrid.ActualWidth;
        var viewHeight = RootGrid.ActualHeight;
        if (viewWidth <= 0 || viewHeight <= 0) return;

        const double cardWidth = 372;
        var cardHeight = DetailPopupContent.ActualHeight > 50 ? DetailPopupContent.ActualHeight : 380;

        // X 方向定位：优先在鼠标右侧 16px，若右侧空间不足则翻转至左侧
        double offsetX;
        if (pointer.X + 16 + cardWidth <= viewWidth - 12)
        {
            offsetX = pointer.X + 16;
        }
        else if (pointer.X - 16 - cardWidth >= 12)
        {
            offsetX = pointer.X - 16 - cardWidth;
        }
        else
        {
            offsetX = Math.Max(12, viewWidth - cardWidth - 12);
        }

        // Y 方向定位：优先将卡片顶部与指针微调对齐，若下方超出则贴紧底部
        double offsetY = pointer.Y - 16;
        if (offsetY + cardHeight > viewHeight - 12)
        {
            offsetY = viewHeight - cardHeight - 12;
        }
        if (offsetY < 12)
        {
            offsetY = 12;
        }

        DetailPopup.HorizontalOffset = offsetX;
        DetailPopup.VerticalOffset = offsetY;
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

    public void FocusSearchBox()
    {
        SearchBox?.Focus(FocusState.Programmatic);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.ApplyFilterNow();
    }
}
