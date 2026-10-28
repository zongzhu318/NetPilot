using CommunityToolkit.Mvvm.ComponentModel;
using NetPilot.App.Services;
using NetPilot.Core.Models;
using NetPilot.Core.Storage;

namespace NetPilot.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private EnumOption<AppTheme> _themeOption;
    private EnumOption<TextEncodingKind> _encodingOption;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        var settings = services.Settings;

        ThemeOptions =
        [
            new EnumOption<AppTheme>(AppTheme.Light, "浅色"),
            new EnumOption<AppTheme>(AppTheme.Dark, "深色")
        ];
        EncodingOptions = OptionLists.Encodings;

        _themeOption = ThemeOptions.First(x => x.Value == settings.Theme);
        _encodingOption = EncodingOptions.First(x => x.Value == settings.DefaultEncoding);

        FontFamily = settings.FontFamily;
        FontSize = settings.FontSize;
        ShowTimestamps = settings.ShowTimestamps;
        AutoScroll = settings.AutoScroll;
        MaxLogEntries = settings.MaxLogEntries;
        MaxGlobalLogEntries = settings.GlobalLogLimit;
        LoopIntervalMs = settings.DefaultLoopIntervalMs;
    }

    public IReadOnlyList<EnumOption<AppTheme>> ThemeOptions { get; }
    public IReadOnlyList<EnumOption<TextEncodingKind>> EncodingOptions { get; }

    public EnumOption<AppTheme> ThemeOption
    {
        get => _themeOption;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _themeOption, value);
            }
        }
    }

    public EnumOption<TextEncodingKind> EncodingOption
    {
        get => _encodingOption;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _encodingOption, value);
            }
        }
    }

    public string FontFamily { get; set; }
    public double FontSize { get; set; }
    public bool ShowTimestamps { get; set; }
    public bool AutoScroll { get; set; }
    public int MaxLogEntries { get; set; }
    public int MaxGlobalLogEntries { get; set; }
    public int LoopIntervalMs { get; set; }

    public void Save()
    {
        _services.SettingsService.Update(settings =>
        {
            settings.Theme = ThemeOption.Value;
            settings.FontFamily = FontFamily.Trim();
            settings.FontSize = Math.Clamp(FontSize, 10, 32);
            settings.ShowTimestamps = ShowTimestamps;
            settings.AutoScroll = AutoScroll;
            settings.DefaultEncoding = EncodingOption.Value;
            settings.MaxLogEntries = Math.Clamp(MaxLogEntries, 100, 200_000);
            settings.GlobalLogLimit = Math.Clamp(MaxGlobalLogEntries, 100, 200_000);
            settings.DefaultLoopIntervalMs = Math.Clamp(LoopIntervalMs, 10, 600_000);
        });

        ThemeManager.Apply(_services.Settings.Theme);
        _services.GlobalLog.SetLimit(_services.Settings.GlobalLogLimit);
    }
}
