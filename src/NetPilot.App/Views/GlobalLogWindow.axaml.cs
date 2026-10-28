using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views;

public partial class GlobalLogWindow : Window
{
    public GlobalLogWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
