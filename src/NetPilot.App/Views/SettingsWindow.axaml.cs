using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NetPilot.App.ViewModels;

namespace NetPilot.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.Save();
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
