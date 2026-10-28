using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Core.Models;

namespace NetPilot.Core.Storage;

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; } = 13;
    public bool ShowTimestamps { get; set; } = true;
    public TextEncodingKind DefaultEncoding { get; set; } = TextEncodingKind.Utf8;
    public int MaxLogEntries { get; set; } = 2000;
    public bool AutoScroll { get; set; } = true;
    public int GlobalLogLimit { get; set; } = 5000;
    public int DefaultLoopIntervalMs { get; set; } = 1000;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.BaseDirectory, "settings.json");
        AppPaths.EnsureCreated();
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(Settings, Options));
        Changed?.Invoke(this, Settings);
    }

    public void Update(Action<AppSettings> update)
    {
        update(Settings);
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
