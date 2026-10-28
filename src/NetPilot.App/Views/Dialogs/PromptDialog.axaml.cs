using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views.Dialogs;

public partial class PromptDialog : Window
{
    private readonly TextBox? _input;

    public PromptDialog()
    {
        InitializeComponent();
        _input = this.FindControl<TextBox>("InputBox");
        if (_input is not null)
        {
            _input.KeyDown += OnInputKeyDown;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static async Task<string?> ShowAsync(Window owner, string title, string label, string defaultValue)
    {
        var dialog = new PromptDialog { Title = title };

        var titleText = dialog.FindControl<TextBlock>("TitleText");
        if (titleText is not null)
        {
            titleText.Text = title;
        }

        var labelText = dialog.FindControl<TextBlock>("LabelText");
        if (labelText is not null)
        {
            labelText.Text = label;
        }

        if (dialog._input is not null)
        {
            dialog._input.Text = defaultValue;
            dialog.Opened += (_, _) =>
            {
                dialog._input.Focus();
                dialog._input.SelectAll();
            };
        }

        var result = await dialog.ShowDialog<string?>(owner);
        return result;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Close(_input?.Text?.Trim());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(_input?.Text?.Trim());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
