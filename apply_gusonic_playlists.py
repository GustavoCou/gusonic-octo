#!/usr/bin/env python3
from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: apply_gusonic_playlists.py <octo-source-root>")

root = Path(sys.argv[1]).resolve()

def load(rel):
    p = root / rel
    if not p.exists():
        raise SystemExit(f"missing expected file: {p}")
    return p, p.read_text(encoding="utf-8")

def save(path, text):
    path.write_text(text, encoding="utf-8")

# ---------------------------------------------------------------------------
# 1) Deezer metadata: add public playlist discovery/detail/track metadata.
#    Audio still comes from Octo's existing YouTube/Soulseek pipeline.
# ---------------------------------------------------------------------------
p, s = load("octo/Services/Metadata/DeezerMetadataService.cs")

if "using Octo.Models.Subsonic;" not in s:
    marker = "using Octo.Models.Settings;\n"
    if marker not in s:
        raise SystemExit("DeezerMetadataService.cs: using marker not found")
    s = s.replace(
        marker,
        marker + "using Octo.Models.Subsonic;\nusing Octo.Services.Common;\n",
        1,
    )

album_detail = """    public record AlbumDetail(string DeezerId, string Title, string Artist,
        string? CoverUrl, int? Year, string? Genre, string? Label, List<AlbumTrack> Tracks);
"""

if "public record PlaylistTrack(" not in s:
    if album_detail not in s:
        raise SystemExit("DeezerMetadataService.cs: AlbumDetail marker not found")
    s = s.replace(
        album_detail,
        album_detail
        + """
    /// <summary>Minimal track metadata from a Deezer playlist. These are converted
    /// into Octo's normal YouTube-preview/Soulseek-heart placeholders by
    /// SoulseekMetadataService; Deezer is metadata-only here.</summary>
    public record PlaylistTrack(string Title, string Artist, int? Duration,
        string? AlbumTitle, string? CoverUrl);
""",
        1,
    )

playlist_methods = r'''
    // ---------------------------------------------------------------------
    // Deezer playlist discovery
    //
    // Octo intentionally uses Deezer only as a metadata/catalog source here.
    // No ARL or Deezer audio endpoint is involved: opening a playlist produces
    // Octo's normal external-song placeholders, so playback still resolves via
    // YouTube and hearts still acquire via Soulseek/Lidarr.
    // ---------------------------------------------------------------------

    public async Task<List<ExternalPlaylist>> SearchPlaylistsAsync(
        string query, int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
            return new List<ExternalPlaylist>();

        // Playlists are represented in Subsonic's album column, so keep the set
        // deliberately small to avoid masking real album results.
        var safeLimit = Math.Clamp(limit, 1, 10);

        using var response = await GetJsonAsync(
            $"{Base}/search/playlist?q={Uri.EscapeDataString(query)}&limit={safeLimit}", ct);

        if (response.Transient || response.Doc is null)
            return new List<ExternalPlaylist>();

        if (!response.Doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return new List<ExternalPlaylist>();

        var playlists = new List<ExternalPlaylist>();
        foreach (var item in data.EnumerateArray())
        {
            var playlist = ParsePlaylist(item);
            if (playlist is not null)
                playlists.Add(playlist);
        }

        return playlists;
    }

    public async Task<ExternalPlaylist?> GetPlaylistAsync(
        string externalProvider, string externalId, CancellationToken ct = default)
    {
        if (!string.Equals(externalProvider, "deezer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(externalId))
            return null;

        using var response = await GetJsonAsync(
            $"{Base}/playlist/{Uri.EscapeDataString(externalId)}", ct);

        if (response.Transient || response.Doc is null)
            return null;

        return ParsePlaylist(response.Doc.RootElement);
    }

    public async Task<List<PlaylistTrack>> GetPlaylistTracksAsync(
        string externalProvider, string externalId, CancellationToken ct = default)
    {
        if (!string.Equals(externalProvider, "deezer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(externalId))
            return new List<PlaylistTrack>();

        using var playlistResponse = await GetJsonAsync(
            $"{Base}/playlist/{Uri.EscapeDataString(externalId)}", ct);

        if (playlistResponse.Transient || playlistResponse.Doc is null)
            return new List<PlaylistTrack>();

        var root = playlistResponse.Doc.RootElement;
        var tracks = new List<PlaylistTrack>();

        string? nextUrl = null;
        if (root.TryGetProperty("tracklist", out var tracklist)
            && tracklist.ValueKind == JsonValueKind.String)
        {
            var raw = tracklist.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
                nextUrl = raw!.Contains('?') ? $"{raw}&limit=1000" : $"{raw}?limit=1000";
        }

        // Fallback for unexpected payloads that already embed tracks.data.
        if (string.IsNullOrWhiteSpace(nextUrl))
        {
            if (root.TryGetProperty("tracks", out var embedded)
                && embedded.TryGetProperty("data", out var embeddedData)
                && embeddedData.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in embeddedData.EnumerateArray())
                    AddPlaylistTrack(tracks, item);
            }
            return tracks;
        }

        // Follow Deezer pagination. This runs only after a user opens a playlist,
        // never on every search keystroke.
        var seenPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(nextUrl) && seenPages.Add(nextUrl))
        {
            using var page = await GetJsonAsync(nextUrl, ct);
            if (page.Transient || page.Doc is null)
                break;

            var pageRoot = page.Doc.RootElement;
            if (!pageRoot.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
                break;

            foreach (var item in data.EnumerateArray())
                AddPlaylistTrack(tracks, item);

            nextUrl = pageRoot.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }

        return tracks;
    }

    private static void AddPlaylistTrack(List<PlaylistTrack> target, JsonElement track)
    {
        var title = Str(track, "title")?.Trim();
        var artist = track.TryGetProperty("artist", out var artistEl)
            ? Str(artistEl, "name")?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return;

        string? albumTitle = null;
        string? coverUrl = null;
        if (track.TryGetProperty("album", out var album)
            && album.ValueKind == JsonValueKind.Object)
        {
            albumTitle = Str(album, "title");
            coverUrl = Str(album, "cover_xl")
                ?? Str(album, "cover_big")
                ?? Str(album, "cover_medium");
        }

        target.Add(new PlaylistTrack(
            title!,
            artist!,
            Int(track, "duration"),
            albumTitle,
            coverUrl));
    }

    private static ExternalPlaylist? ParsePlaylist(JsonElement playlist)
    {
        if (!playlist.TryGetProperty("id", out var idEl)
            || idEl.ValueKind != JsonValueKind.Number
            || !playlist.TryGetProperty("title", out var titleEl)
            || titleEl.ValueKind != JsonValueKind.String)
            return null;

        var externalId = idEl.GetInt64().ToString();
        var title = titleEl.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(title))
            return null;

        string? curator = null;
        if (playlist.TryGetProperty("user", out var user)
            && user.ValueKind == JsonValueKind.Object)
            curator = Str(user, "name");

        if (string.IsNullOrWhiteSpace(curator)
            && playlist.TryGetProperty("creator", out var creator)
            && creator.ValueKind == JsonValueKind.Object)
            curator = Str(creator, "name");

        DateTime? created = null;
        var creationDate = Str(playlist, "creation_date");
        if (!string.IsNullOrWhiteSpace(creationDate)
            && DateTime.TryParse(creationDate, out var parsedDate))
            created = parsedDate;

        var cover = Str(playlist, "picture_xl")
            ?? Str(playlist, "picture_big")
            ?? Str(playlist, "picture_medium");

        return new ExternalPlaylist
        {
            Id = PlaylistIdHelper.CreatePlaylistId("deezer", externalId),
            Name = title,
            Description = Str(playlist, "description"),
            CuratorName = curator,
            Provider = "deezer",
            ExternalId = externalId,
            TrackCount = Int(playlist, "nb_tracks") ?? 0,
            Duration = Int(playlist, "duration") ?? 0,
            CoverUrl = cover,
            CreatedDate = created,
        };
    }

'''

if "public async Task<List<ExternalPlaylist>> SearchPlaylistsAsync(" not in s:
    method_marker = "    private Task<(int? Year, bool Transient)> AlbumYearAsync(long albumId, CancellationToken ct)\n"
    if method_marker not in s:
        raise SystemExit("DeezerMetadataService.cs: AlbumYearAsync marker not found")
    s = s.replace(method_marker, playlist_methods + method_marker, 1)

save(p, s)

# ---------------------------------------------------------------------------
# 2) Soulseek metadata facade: wire playlist metadata into Octo's existing
#    external-song routing. Playlist tracks become normal Octo placeholders:
#    Play => YouTube and Heart => Soulseek/Lidarr.
# ---------------------------------------------------------------------------
p, s = load("octo/Services/Soulseek/SoulseekMetadataService.cs")

old = """    public Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
        => Task.FromResult(new List<ExternalPlaylist>());

    public Task<ExternalPlaylist?> GetPlaylistAsync(string externalProvider, string externalId)
        => Task.FromResult<ExternalPlaylist?>(null);

    public Task<List<Song>> GetPlaylistTracksAsync(string externalProvider, string externalId)
        => Task.FromResult(new List<Song>());
"""

new = """    public Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
        => _deezer.SearchPlaylistsAsync(query, Math.Min(limit, 10));

    public Task<ExternalPlaylist?> GetPlaylistAsync(string externalProvider, string externalId)
    {
        if (!string.Equals(externalProvider, "deezer", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<ExternalPlaylist?>(null);

        return _deezer.GetPlaylistAsync(externalProvider, externalId);
    }

    public async Task<List<Song>> GetPlaylistTracksAsync(string externalProvider, string externalId)
    {
        if (!string.Equals(externalProvider, "deezer", StringComparison.OrdinalIgnoreCase))
            return new List<Song>();

        var playlist = await _deezer.GetPlaylistAsync(externalProvider, externalId);
        var sourceTracks = await _deezer.GetPlaylistTracksAsync(externalProvider, externalId);
        if (sourceTracks.Count == 0) return new List<Song>();

        var songs = new List<Song>(sourceTracks.Count);
        foreach (var source in sourceTracks)
        {
            var placeholder = (await SearchSongsByArtistTitleAsync(
                source.Artist, source.Title, 1, source.Duration)).FirstOrDefault();

            if (placeholder is null) continue;

            // The client sees the external playlist as an album.
            placeholder.Album = playlist?.Name ?? source.AlbumTitle ?? "";
            placeholder.AlbumId = playlist?.Id;
            placeholder.Track = songs.Count + 1;
            placeholder.TotalTracks = sourceTracks.Count;
            placeholder.CoverArtUrl = playlist?.CoverUrl ?? source.CoverUrl;
            placeholder.CoverArtUrlLarge = playlist?.CoverUrl ?? source.CoverUrl;

            // Preserve the real release on the routing object for heart/download tags.
            var routing = _idRegistry.Lookup(placeholder.Id);
            if (routing is not null && !string.IsNullOrWhiteSpace(source.AlbumTitle))
                routing.Album = source.AlbumTitle;

            songs.Add(placeholder);
        }

        return songs;
    }
"""

if old in s:
    s = s.replace(old, new, 1)
elif "_deezer.SearchPlaylistsAsync(query" not in s:
    raise SystemExit("SoulseekMetadataService.cs: playlist stub block not found")

save(p, s)

# ---------------------------------------------------------------------------
# 3) Keep playlist hits to 5 rows so Arpeggi gets playlists without burying
#    actual album matches in Subsonic's shared album column.
# ---------------------------------------------------------------------------
p, s = load("octo/Controllers/SubSonicController.cs")

old = """        var playlistTask = _subsonicSettings.EnableExternalPlaylists
            ? await _metadataService.SearchPlaylistsAsync(cleanQuery, requestedAlbums)
            : new List<ExternalPlaylist>();
"""
new = """        var playlistTask = _subsonicSettings.EnableExternalPlaylists
            ? await _metadataService.SearchPlaylistsAsync(cleanQuery, Math.Min(requestedAlbums, 5))
            : new List<ExternalPlaylist>();
"""

if old in s:
    s = s.replace(old, new, 1)
elif "Math.Min(requestedAlbums, 5)" not in s:
    raise SystemExit("SubSonicController.cs: playlist search block not found")

save(p, s)

print("Gusonic Deezer playlist integration applied successfully.")
