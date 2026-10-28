using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views.Controls;

public partial class ClientListPanel : UserControl
{
    public ClientListPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
