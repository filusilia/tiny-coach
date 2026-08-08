using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AegisCoach.Native.Converters;

/// <summary>赛后结果（bool?）到徽章画刷的转换：胜=绿，负=红，未知=中性。</summary>
public sealed class ResultToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            true => (Brush)Application.Current.Resources["CoachSuccessBrush"],
            false => (Brush)Application.Current.Resources["CoachDangerBrush"],
            _ => (Brush)Application.Current.Resources["CoachNeutralBrush"],
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
