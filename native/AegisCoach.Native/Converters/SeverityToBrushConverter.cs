using AegisCoach.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AegisCoach.Native.Converters;

/// <summary>把提醒严重级别映射为主题色画刷。</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value switch
        {
            AdviceSeverity.Critical => "CriticalBrush",
            AdviceSeverity.Warning => "WarningBrush",
            _ => "InfoBrush",
        };

        if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
