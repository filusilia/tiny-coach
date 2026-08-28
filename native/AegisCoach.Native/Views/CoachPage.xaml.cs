using AegisCoach.Native.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AegisCoach.Native.Views;

/// <summary>
/// 实时教练页：对局状态条 + 自身英雄数值/装备 + 规则提醒流 + 阵容与出装/克制选人推荐。
/// </summary>
public sealed partial class CoachPage : Page
{
    public CoachViewModel ViewModel { get; } = new();

    public CoachPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void CoachPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = ActualWidth > 0 && ActualWidth < 1100;
        ApplyResponsiveLayout(compact);
    }

    public void ApplyResponsiveLayout(bool compact)
    {
        CoachScrollViewer.Padding = compact ? new Thickness(12, 12, 12, 16) : new Thickness(24, 18, 24, 22);
        if (compact)
        {
            CoachPrimaryGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            CoachPrimaryGrid.ColumnDefinitions[0].MinWidth = 0;
            CoachPrimaryGrid.ColumnDefinitions[1].Width = new GridLength(0);
            CoachPrimaryGrid.ColumnDefinitions[1].MinWidth = 0;
            Place(CoachHeroPanel, 0, 0);
            Place(CoachAdvicePanel, 1, 0);

            StackThreeColumns(CoachRosterGrid, CoachAlliesPanel, CoachEnemiesPanel, CoachRecommendationPanel);
            StackThreeColumns(CoachCounterGrid, CoachCounterRiskPanel, CoachCounterAdvantagePanel, CoachCounterPickPanel);

            Place(CoachStatusLeft, 0, 0);
            Place(CoachActionsPanel, 0, 2);
            Place(CoachOverviewPanel, 1, 0, 3);
        }
        else
        {
            CoachPrimaryGrid.ColumnDefinitions[0].Width = new GridLength(0.95, GridUnitType.Star);
            CoachPrimaryGrid.ColumnDefinitions[0].MinWidth = 320;
            CoachPrimaryGrid.ColumnDefinitions[1].Width = new GridLength(1.05, GridUnitType.Star);
            CoachPrimaryGrid.ColumnDefinitions[1].MinWidth = 360;
            Place(CoachHeroPanel, 0, 0);
            Place(CoachAdvicePanel, 0, 1);

            RestoreThreeColumns(CoachRosterGrid, CoachAlliesPanel, CoachEnemiesPanel, CoachRecommendationPanel, 180, 180, 260);
            RestoreThreeColumns(CoachCounterGrid, CoachCounterRiskPanel, CoachCounterAdvantagePanel, CoachCounterPickPanel, 200, 200, 200);

            Place(CoachStatusLeft, 0, 0);
            Place(CoachOverviewPanel, 0, 1);
            Place(CoachActionsPanel, 0, 2);
        }
    }

    private static void StackThreeColumns(Grid grid, FrameworkElement first, FrameworkElement second, FrameworkElement third)
    {
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[0].MinWidth = 0;
        for (var index = 1; index < 3; index++)
        {
            grid.ColumnDefinitions[index].Width = new GridLength(0);
            grid.ColumnDefinitions[index].MinWidth = 0;
        }
        Place(first, 0, 0);
        Place(second, 1, 0);
        Place(third, 2, 0);
    }

    private static void RestoreThreeColumns(
        Grid grid,
        FrameworkElement first,
        FrameworkElement second,
        FrameworkElement third,
        double firstMin,
        double secondMin,
        double thirdMin)
    {
        var weights = new[] { 1d, 1d, 1.3d };
        var minimums = new[] { firstMin, secondMin, thirdMin };
        for (var index = 0; index < 3; index++)
        {
            grid.ColumnDefinitions[index].Width = new GridLength(weights[index], GridUnitType.Star);
            grid.ColumnDefinitions[index].MinWidth = minimums[index];
        }
        Place(first, 0, 0);
        Place(second, 0, 1);
        Place(third, 0, 2);
    }

    private static void Place(FrameworkElement element, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }
}
