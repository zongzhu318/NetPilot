using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace NetPilot.App.Views.Session;

public partial class HttpSessionView : UserControl
{
    public HttpSessionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
