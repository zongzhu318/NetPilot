using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Core.Models;

namespace NetPilot.Core.Storage;

public sealed class FavoriteEntry
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public SessionConfig Config { get; set; } = new();
}

public sealed class FavoriteStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public FavoriteStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.BaseDirectory, "favorites.json");
        AppPaths.EnsureCreated();
        Favorites = Load();
    }

    public List<FavoriteEntry> Favorites { get; }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(Favorites, Options));
    }

    public void AddOrUpdate(FavoriteEntry entry)
    {
        var existing = Favorites.FirstOrDefault(x =>
            string.Equals(x.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Config = entry.Config;
            existing.CreatedAt = DateTime.Now;
        }
        else
        {
            Favorites.Insert(0, entry);
        }

        Save();
    }

    public bool Remove(string name)
    {
        var item = Favorites.FirstOrDefault(x => x.Name == name);
        if (item is null)
        {
            return false;
        }

        Favorites.Remove(item);
        Save();
        return true;
    }

    private List<FavoriteEntry> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<FavoriteEntry>>(File.ReadAllText(_path), Options) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
