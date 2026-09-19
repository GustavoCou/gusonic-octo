using Octo.Models.Subsonic;
using Octo.Services.Common;

namespace Octo.Tests;

public class SearchCollectionCacheTests
{
    [Fact]
    public void Remember_StoresPlaylistsAndGenresPerUser()
    {
        var cache = new SearchCollectionCache();

        cache.Remember(
            "alice",
            "night drive",
            [
                new ExternalPlaylist
                {
                    Id = "pl-deezer-123",
                    Name = "Night Drive",
                    Provider = "deezer",
                    ExternalId = "123"
                }
            ],
            ["electronic", "night"]);

        var snapshot = cache.Get("alice");

        Assert.Equal("night drive", snapshot.Query);
        Assert.Single(snapshot.Playlists);
        Assert.Equal("pl-deezer-123", snapshot.Playlists[0].Id);
        Assert.Equal(["electronic", "night"], snapshot.Genres);
    }

    [Fact]
    public void Remember_DeduplicatesAndNormalizesCollections()
    {
        var cache = new SearchCollectionCache();
        var playlist = new ExternalPlaylist
        {
            Id = "pl-deezer-123",
            Name = "Mix",
            Provider = "deezer",
            ExternalId = "123"
        };

        cache.Remember(
            "Alice",
            "mix",
            [playlist, playlist],
            [" House ", "house", "", "  "]);

        var snapshot = cache.Get("alice");

        Assert.Single(snapshot.Playlists);
        Assert.Single(snapshot.Genres);
        Assert.Equal("House", snapshot.Genres[0]);
    }

    [Fact]
    public void Get_IsolatedByUser()
    {
        var cache = new SearchCollectionCache();
        cache.Remember("alice", "query", [], ["jazz"]);

        Assert.Single(cache.Get("alice").Genres);
        Assert.Empty(cache.Get("bob").Genres);
    }

    [Fact]
    public void Remember_RequiresAuthenticatedUserAndQuery()
    {
        var cache = new SearchCollectionCache();

        cache.Remember("", "rock", [], ["rock"]);
        cache.Remember("alice", "   ", [], ["rock"]);

        Assert.Empty(cache.Get("").Genres);
        Assert.Empty(cache.Get("alice").Genres);
    }
}
