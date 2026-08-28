using AegisCoach.Native.Models;
using AegisCoach.Native.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AegisCoach.Native.Views;

/// <summary>
/// 英雄资料库与详情页：英雄网格 + 属性分栏 + 技能/天赋/出装/对阵详情。
/// 数据加载由 MainWindow 在设置就绪后触发（遵守首次启动门控）。
/// </summary>
public sealed partial class HeroesPage : Page
{
    public HeroesViewModel ViewModel { get; } = new();

    public HeroesPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += HeroesPage_Loaded;
    }

    private void HeroesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (AttributeTabs.SelectedIndex < 0)
        {
            AttributeTabs.SelectedIndex = 0;
        }
    }

    /// <summary>由 MainWindow 在设置保存成功后调用（force=true 重新联网拉取）。</summary>
    public Task LoadAsync(bool force) => ViewModel.LoadCommand.ExecuteAsync(force);

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Cancel();
    }

    private void HeroListHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateHeroCardLayout(HeroListHost.ActualWidth);
    }

    private async void HeroGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is GridView grid && grid.SelectedItem is HeroCardItem item)
        {
            await ViewModel.SelectHeroAsync(item);
            grid.SelectedItem = null;
            HeroDetailHost.ChangeView(0, 0, null, disableAnimation: true);
        }
    }

    private void AttributeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AttributeTabs.SelectedItem is HeroSection section)
        {
            ViewModel.SelectSection(section.Id);
        }
    }

    private void TalentTile_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var scrollOffset = HeroDetailHost.VerticalOffset;
        ViewModel.SelectTalents();
        // 锁定当前滚动位置，防止因子元素布局变动引起任何位置漂移
        HeroDetailHost.ChangeView(null, scrollOffset, null, disableAnimation: true);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            HeroDetailHost.ChangeView(null, scrollOffset, null, disableAnimation: true);
        });
        e.Handled = true;
    }

    private void AbilityTile_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AbilityDisplay ability })
        {
            var scrollOffset = HeroDetailHost.VerticalOffset;
            ViewModel.SelectAbility(ability);
            // 锁定当前滚动位置，防止因子元素布局变动引起任何位置漂移
            HeroDetailHost.ChangeView(null, scrollOffset, null, disableAnimation: true);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                HeroDetailHost.ChangeView(null, scrollOffset, null, disableAnimation: true);
            });
            e.Handled = true;
        }
    }

    private void AbilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AbilityDisplay ability })
        {
            var scrollOffset = HeroDetailHost.VerticalOffset;
            ViewModel.SelectAbility(ability);
            HeroDetailHost.ChangeView(null, scrollOffset, null, disableAnimation: true);
        }
    }

    private void HeroDetailHost_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        // 阻断子元素因绑定更新或焦点切换自发请求的视图定位，彻底杜绝跳回页面顶部
        args.Handled = true;
    }

    private void AbilitiesScrollViewer_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        args.Handled = true;
    }

    private void SelectedAbilityPanel_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        args.Handled = true;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.ApplyFilterNow();
    }
}
