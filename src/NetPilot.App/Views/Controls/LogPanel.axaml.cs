using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NetPilot.App.ViewModels;

namespace NetPilot.App.Views.Controls;

public partial class LogPanel : UserControl
{
    private SessionViewModel? _session;
    private bool _scrollPending;
    private readonly ListBox? _list;

    public LogPanel()
    {
        InitializeComponent();
        _list = this.FindControl<ListBox>("LogList");
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_session is not null)
        {
            _session.Logs.CollectionChanged -= OnLogsChanged;
        }

        _session = DataContext as SessionViewModel;
        if (_session is not null)
        {
            _session.Logs.CollectionChanged += OnLogsChanged;
        }
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_session is not { AutoScroll: true } || _scrollPending)
        {
            return;
        }

        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            if (_list is not null && _session is { AutoScroll: true } && _list.ItemCount > 0)
            {
                _list.ScrollIntoView(_list.ItemCount - 1);
            }
        }, DispatcherPriority.Background);
    }
}
