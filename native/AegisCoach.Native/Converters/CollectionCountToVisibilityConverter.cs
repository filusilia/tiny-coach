using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AegisCoach.Native.Converters;

/// <summary>集合元素个数大于 0 时显示，否则折叠。</summary>
public sealed class CollectionCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasItems = value switch
        {
            int count => count > 0,
            ICollection collection => collection.Count > 0,
            IEnumerable enumerable => enumerable.Cast<object>().Any(),
            _ => false,
        };
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
