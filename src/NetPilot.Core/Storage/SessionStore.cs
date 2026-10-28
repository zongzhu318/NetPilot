using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Core.Models;

namespace NetPilot.Core.Storage;

public static class AppPaths
{
    private static string? _baseDirectory;

    public static string BaseDirectory
    {
        get
        {
            if (_baseDirectory is not null)
            {
                return _baseDirectory;
            }

            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }

            _baseDirectory = Path.Combine(root, "NetPilot");
            return _baseDirectory;
        }
    }

    public static void OverrideBaseDirectory(string path) => _baseDirectory = path;

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(Path.Combine(BaseDirectory, "sessions"));
    }
}

public static class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<SessionConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<SessionConfig>(stream, Options, cancellationToken)
            .ConfigureAwait(false);
        return config ?? throw new InvalidDataException("会话文件内容为空或格式不正确");
    }

    public static async Task SaveAsync(string path, SessionConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, config, Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, true);
    }

    public static string DefaultFileName(string sessionName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(sessionName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return (string.IsNullOrWhiteSpace(name) ? "session" : name) + ".np.json";
    }
}
