using Avalonia;
using Avalonia.Styling;
using NetPilot.Core.Models;

namespace NetPilot.App.Services;

public static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
