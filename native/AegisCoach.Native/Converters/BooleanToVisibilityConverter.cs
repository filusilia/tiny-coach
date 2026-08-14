using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AegisCoach.Native.Converters;

/// <summary>bool -&gt; Visibility；设置 Invert=true 时反转（仅悬浮卡模板使用）。</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
