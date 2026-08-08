using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AegisCoach.Native.Converters;

/// <summary>集合元素个数大于 0 时显示，否则折叠。</summary>
public sealed class CollectionCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is ICollection { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
