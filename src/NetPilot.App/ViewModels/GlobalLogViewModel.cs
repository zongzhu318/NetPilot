using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetPilot.App.Services;

namespace NetPilot.App.ViewModels;

public sealed class GlobalLogViewModel : ObservableObject, IDisposable
{
    private readonly GlobalLogService _service;
    private string _filter = string.Empty;

    public GlobalLogViewModel(GlobalLogService service)
    {
        _service = service;
        Items = new ObservableCollection<GlobalLogEntry>();
        ClearCommand = new RelayCommand(Clear);
        ((INotifyCollectionChanged)service.Entries).CollectionChanged += OnSourceChanged;
        Rebuild();
    }

    public ObservableCollection<GlobalLogEntry> Items { get; }
    public RelayCommand ClearCommand { get; }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Rebuild();
            }
        }
    }

    public string CountText => $"{Items.Count:N0} 条事件";

    public bool HasItems => Items.Count > 0;

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (GlobalLogEntry entry in e.NewItems)
                {
                    if (entry.Matches(Filter))
                    {
                        Items.Add(entry);
                    }
                }

                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (GlobalLogEntry entry in e.OldItems)
                {
                    Items.Remove(entry);
                }

                break;
            case NotifyCollectionChangedAction.Reset:
                Rebuild();
                break;
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasItems));
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var entry in _service.Entries)
        {
            if (entry.Matches(Filter))
            {
                Items.Add(entry);
            }
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasItems));
    }

    private void Clear()
    {
        _service.Clear();
        Rebuild();
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_service.Entries).CollectionChanged -= OnSourceChanged;
    }
}
