using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LaptopThermalHelper.App.Controls;

/// <summary>
/// Raises one reset notification for a complete data replacement. This keeps
/// chart controls from recalculating once per individual sample.
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
