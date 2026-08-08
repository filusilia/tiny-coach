using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AegisCoach.Native.Converters;

/// <summary>字符串为空时返回 Collapsed，否则返回 Visible。</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
