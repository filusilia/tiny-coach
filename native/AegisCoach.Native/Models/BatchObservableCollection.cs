using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AegisCoach.Native.Models;

/// <summary>
/// 支持批量添加的 ObservableCollection，通过单次 Reset 通知避免大量条目逐一触发 UI 重排。
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
