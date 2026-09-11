using AegisCoach.Core.Models;
using AegisCoach.Native.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AegisCoach.Native.Views;

/// <summary>
/// 赛后分析页面：展示历史对局列表、习惯数据、AI 复盘与操作记录。
/// </summary>
public sealed partial class AnalysisPage : Page
{
    public AnalysisViewModel ViewModel { get; } = new();

    public AnalysisPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.IsDetailVisible) or nameof(ViewModel.SelectedSummary))
        {
            var compact = ActualWidth > 0 && ActualWidth < 1100;
            ApplyResponsiveLayout(compact);
        }
    }

    private void AnalysisPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = ActualWidth > 0 && ActualWidth < 1100;
        ApplyResponsiveLayout(compact);
    }

    public void ApplyResponsiveLayout(bool compact)
    {
        AnalysisRootGrid.Padding = compact ? new Thickness(12, 12, 12, 16) : new Thickness(24, 18, 24, 22);

        var hasDetail = ViewModel.IsDetailVisible;
        if (compact)
        {
            if (!hasDetail)
            {
                ReportsListView.Visibility = Visibility.Visible;
                AnalysisListColumn.Width = new GridLength(1, GridUnitType.Star);
                AnalysisListColumn.MinWidth = 0;
                AnalysisDetailColumn.Width = new GridLength(0);
                AnalysisDetailColumn.MinWidth = 0;
            }
            else
            {
                ReportsListView.Visibility = Visibility.Collapsed;
                AnalysisListColumn.Width = new GridLength(0);
                AnalysisListColumn.MinWidth = 0;
                AnalysisDetailColumn.Width = new GridLength(1, GridUnitType.Star);
                AnalysisDetailColumn.MinWidth = 0;
            }

            Grid.SetRow(AnalysisImportPanel, 1);
            Grid.SetColumn(AnalysisImportPanel, 0);
            AnalysisImportPanel.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            ReportsListView.Visibility = Visibility.Visible;
            AnalysisListColumn.Width = new GridLength(330);
            AnalysisListColumn.MinWidth = 280;
            AnalysisDetailColumn.Width = new GridLength(1, GridUnitType.Star);
            AnalysisDetailColumn.MinWidth = 360;

            Grid.SetRow(AnalysisImportPanel, 0);
            Grid.SetColumn(AnalysisImportPanel, 0);
            AnalysisImportPanel.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }

    private async void ReportsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is MatchReportSummary summary)
        {
            await ViewModel.SelectSummaryAsync(summary);
        }
        else if (ReportsListView.SelectedItem is null)
        {
            await ViewModel.SelectSummaryAsync(null);
        }
    }

    public void Cancel() => ViewModel.Cancel();
}
