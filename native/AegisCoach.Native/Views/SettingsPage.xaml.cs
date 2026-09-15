using AegisCoach.Native.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AegisCoach.Native.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();
    private bool? _currentCompactState;
    private EventHandler<object>? _renderingPump;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
        SizeChanged += SettingsPage_SizeChanged;
        RegisterPropertyChangedCallback(VisibilityProperty, (s, e) =>
        {
            if (Visibility == Visibility.Visible)
            {
                StartInputPump();
            }
            else
            {
                StopInputPump();
            }
        });
    }

    public void StartInputPump()
    {
        if (_renderingPump == null)
        {
            _renderingPump = (_, _) => { };
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += _renderingPump;
        }
    }

    public void StopInputPump()
    {
        if (_renderingPump != null)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= _renderingPump;
            _renderingPump = null;
        }
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLayoutForCurrentSize();
        if (Visibility == Visibility.Visible)
        {
            StartInputPump();
        }
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopInputPump();
    }

    private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayoutForCurrentSize();
    }

    public void UpdateLayoutForCurrentSize()
    {
        if (ActualWidth > 0)
        {
            ApplyResponsiveLayout(ActualWidth < 1040);
        }
    }

    public void ApplyResponsiveLayout(bool compact)
    {
        if (_currentCompactState == compact) return;
        _currentCompactState = compact;

        SettingsScrollViewer.Padding = compact ? new Thickness(14, 12, 14, 24) : new Thickness(24, 18, 24, 32);
        if (compact)
        {
            ContentGrid.ColumnSpacing = 0;
            LeftCol.Width = new GridLength(1, GridUnitType.Star);
            LeftCol.MinWidth = 0;
            RightCol.Width = new GridLength(0);
            RightCol.MinWidth = 0;
            Grid.SetColumn(LeftColumnPanel, 0);
            Grid.SetRow(LeftColumnPanel, 0);
            Grid.SetColumn(RightColumnPanel, 0);
            Grid.SetRow(RightColumnPanel, 1);
        }
        else
        {
            ContentGrid.ColumnSpacing = 20;
            LeftCol.Width = new GridLength(1.08, GridUnitType.Star);
            LeftCol.MinWidth = 340;
            RightCol.Width = new GridLength(0.92, GridUnitType.Star);
            RightCol.MinWidth = 300;
            Grid.SetColumn(LeftColumnPanel, 0);
            Grid.SetRow(LeftColumnPanel, 0);
            Grid.SetColumn(RightColumnPanel, 1);
            Grid.SetRow(RightColumnPanel, 0);
        }
    }
}
