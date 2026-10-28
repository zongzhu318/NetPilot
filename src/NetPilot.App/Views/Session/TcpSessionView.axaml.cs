using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views.Session;

public partial class TcpSessionView : UserControl
{
    public TcpSessionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
