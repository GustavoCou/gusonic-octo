using System.Collections.Concurrent;
using Octo.Models.Subsonic;

namespace Octo.Services.Common;

/// <summary>
/// Short-lived per-user bridge between search3 and collection endpoints.
///
/// Subsonic search2/search3 has no playlist or genre result arrays. Modern clients
/// therefore fetch playlists/genres separately. This cache lets a dynamic external
/// search discovered through search3 surface through getPlaylists/getGenres as native
/// collection types instead of pretending everything is an album.
///
/// Entries are intentionally short lived and never persisted.
/// </summary>
public sealed class SearchCollectionCache
{
    private sealed record Entry(
        string Query,
        DateTime ExpiresUtc,
        IReadOnlyList<ExternalPlaylist> Playlists,
        IReadOnlyList<string> Genres);

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public sealed record Snapshot(
        string Query,
        IReadOnlyList<ExternalPlaylist> Playlists,
        IReadOnlyList<string> Genres)
    {
        public static readonly Snapshot Empty =
            new(string.Empty, Array.Empty<ExternalPlaylist>(), Array.Empty<string>());
    }

    public void Remember(
        string username,
        string query,
        IEnumerable<ExternalPlaylist>? playlists,
        IEnumerable<string>? genres)
    {
        var key = NormalizeUser(username);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(query)) return;

        var playlistList = (playlists ?? Array.Empty<ExternalPlaylist>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
            .ToList();

        var genreList = (genres ?? Array.Empty<string>())
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        _entries[key] = new Entry(
            query.Trim(),
            DateTime.UtcNow.Add(Lifetime),
            playlistList,
            genreList);
    }

    public Snapshot Get(string username)
    {
        var key = NormalizeUser(username);
        if (key.Length == 0 || !_entries.TryGetValue(key, out var entry))
            return Snapshot.Empty;

        if (entry.ExpiresUtc <= DateTime.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return Snapshot.Empty;
        }

        return new Snapshot(entry.Query, entry.Playlists, entry.Genres);
    }

    private static string NormalizeUser(string username) =>
        (username ?? string.Empty).Trim().ToLowerInvariant();
}
