using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views.Controls;

public partial class SendPanel : UserControl
{
    public SendPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
