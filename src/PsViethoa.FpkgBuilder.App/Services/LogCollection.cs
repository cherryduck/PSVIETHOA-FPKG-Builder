using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.App.Services;

/// <summary>ObservableCollection có cắt bớt hàng loạt (một thông báo Reset thay vì hàng nghìn Remove).</summary>
public sealed class LogCollection : ObservableCollection<LogEntry>
{
    public const int Capacity = 20000;
    private const int TrimBatch = 5000;

    public void AddBatch(IEnumerable<LogEntry> entries)
    {
        foreach (var entry in entries)
        {
            Add(entry);
        }

        if (Count > Capacity)
        {
            TrimFront(TrimBatch);
        }
    }

    private void TrimFront(int count)
    {
        var remove = Math.Min(count, Items.Count);
        for (var i = 0; i < remove; i++)
        {
            Items.RemoveAt(0);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
