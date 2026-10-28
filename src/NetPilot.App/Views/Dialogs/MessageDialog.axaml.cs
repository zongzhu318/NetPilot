using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace NetPilot.App.Views.Dialogs;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static async Task<bool> ShowAsync(Window owner, string title, string message, bool showCancel,
        bool danger, bool isError)
    {
        var dialog = new MessageDialog
        {
            Title = title
        };

        var titleText = dialog.FindControl<TextBlock>("TitleText");
        if (titleText is not null)
        {
            titleText.Text = title;
            if (isError)
            {
                titleText.Foreground = dialog.FindResource("DangerBrush") as IBrush;
            }
        }

        var messageText = dialog.FindControl<TextBlock>("MessageText");
        if (messageText is not null)
        {
            messageText.Text = message;
        }

        var cancelButton = dialog.FindControl<Button>("CancelButton");
        if (cancelButton is not null)
        {
            cancelButton.IsVisible = showCancel;
        }

        var okButton = dialog.FindControl<Button>("OkButton");
        if (okButton is not null && danger)
        {
            okButton.Classes.Set("danger", true);
            okButton.Classes.Set("primary", false);
        }

        var result = await dialog.ShowDialog<bool>(owner);
        return result;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
