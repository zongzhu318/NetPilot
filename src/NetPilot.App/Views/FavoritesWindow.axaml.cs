using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views;

public partial class FavoritesWindow : Window
{
    public FavoritesWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
