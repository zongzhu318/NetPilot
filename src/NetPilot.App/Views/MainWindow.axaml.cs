using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NetPilot.App.ViewModels;

namespace NetPilot.App.Views;

public partial class MainWindow : Window
{
    private bool _shuttingDown;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_shuttingDown)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _ = ShutdownAndCloseAsync();
    }

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                var shutdown = viewModel.ShutdownAsync();
                await Task.WhenAny(shutdown, Task.Delay(4000));
            }
        }
        catch
        {
            // ignored
        }

        Close();
    }
}
