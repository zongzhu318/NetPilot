using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views.Session;

public partial class WebSocketSessionView : UserControl
{
    public WebSocketSessionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
