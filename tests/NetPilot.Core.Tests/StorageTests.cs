using NetPilot.Core.Models;
using NetPilot.Core.Storage;
using Xunit;

namespace NetPilot.Core.Tests;

[Collection("Storage")]
public sealed class StorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-tests-{Guid.NewGuid():N}");

    public StorageTests() => AppPaths.OverrideBaseDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch
        {
            // ignored
        }
    }

    [Fact]
    public async Task SessionStore_RoundTripsAllFields()
    {
        var path = Path.Combine(_directory, "session.np.json");
        var config = new SessionConfig
        {
            SessionName = "测试会话",
            Protocol = ProtocolKind.WebSocket,
            Mode = SessionMode.Server,
            Host = "10.0.0.5",
            Port = 9001,
            UseTls = true,
            AutoReconnect = true,
            SendFormat = SendFormat.Hex,
            Newline = NewlineMode.Crlf,
            HistoryList = ["one", "two"],
            WsHeaders = [new HeaderItem("Origin", "https://example.com")],
            HttpHeaders = [new HeaderItem("X-Token", "abc", false)],
            MockStatus = 201
        };

        await SessionStore.SaveAsync(path, config);
        var loaded = await SessionStore.LoadAsync(path);

        Assert.Equal(config.SessionName, loaded.SessionName);
        Assert.Equal(ProtocolKind.WebSocket, loaded.Protocol);
        Assert.Equal(SessionMode.Server, loaded.Mode);
        Assert.Equal("10.0.0.5", loaded.Host);
        Assert.Equal(9001, loaded.Port);
        Assert.True(loaded.UseTls);
        Assert.True(loaded.AutoReconnect);
        Assert.Equal(SendFormat.Hex, loaded.SendFormat);
        Assert.Equal(NewlineMode.Crlf, loaded.Newline);
        Assert.Equal(new[] { "one", "two" }, loaded.HistoryList);
        var header = Assert.Single(loaded.WsHeaders);
        Assert.Equal("Origin", header.Key);
        Assert.Equal("https://example.com", header.Value);
        Assert.False(loaded.HttpHeaders[0].Enabled);
        Assert.Equal(201, loaded.MockStatus);
        Assert.Equal("WebSocket", File.ReadAllText(path).Split("\"Protocol\": \"")[1].Split('"')[0]);
    }

    [Fact]
    public async Task SessionStore_LoadMissing_Throws() =>
        await Assert.ThrowsAnyAsync<IOException>(() =>
            SessionStore.LoadAsync(Path.Combine(_directory, "missing.np.json")));

    [Fact]
    public void DefaultFileName_SanitizesInvalidChars()
    {
        var name = SessionStore.DefaultFileName("a/b:c*?");
        Assert.EndsWith(".np.json", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.Equal("session.np.json", SessionStore.DefaultFileName("   "));
    }

    [Fact]
    public void SettingsService_PersistsAndReloads()
    {
        var path = Path.Combine(_directory, "settings.json");
        var service = new SettingsService(path);
        service.Update(s =>
        {
            s.Theme = AppTheme.Dark;
            s.FontSize = 15;
            s.DefaultEncoding = TextEncodingKind.Gbk;
            s.MaxLogEntries = 500;
        });

        var reloaded = new SettingsService(path).Settings;
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.Equal(15, reloaded.FontSize);
        Assert.Equal(TextEncodingKind.Gbk, reloaded.DefaultEncoding);
        Assert.Equal(500, reloaded.MaxLogEntries);
    }

    [Fact]
    public void FavoriteStore_AddUpdateRemove()
    {
        var path = Path.Combine(_directory, "favorites.json");
        var store = new FavoriteStore(path);
        store.AddOrUpdate(new FavoriteEntry
        {
            Name = "收藏 A",
            Config = new SessionConfig { Host = "1.1.1.1", Port = 1 }
        });
        store.AddOrUpdate(new FavoriteEntry
        {
            Name = "收藏 A",
            Config = new SessionConfig { Host = "2.2.2.2", Port = 2 }
        });

        var entry = Assert.Single(store.Favorites);
        Assert.Equal("2.2.2.2", entry.Config.Host);
        Assert.Equal(2, entry.Config.Port);

        var reloaded = new FavoriteStore(path);
        Assert.Equal("2.2.2.2", Assert.Single(reloaded.Favorites).Config.Host);

        Assert.True(reloaded.Remove("收藏 A"));
        Assert.False(reloaded.Remove("收藏 A"));
        Assert.Empty(new FavoriteStore(path).Favorites);
    }

    [Fact]
    public void SessionConfig_Clone_CopiesListsIndependently()
    {
        var config = new SessionConfig
        {
            HistoryList = ["a"],
            WsHeaders = [new HeaderItem("k", "v")]
        };
        var clone = config.Clone();
        clone.HistoryList.Add("b");
        clone.WsHeaders[0].Value = "changed";

        Assert.Single(config.HistoryList);
        Assert.Equal("v", config.WsHeaders[0].Value);
        Assert.Equal(2, clone.HistoryList.Count);
    }
}
