# Gusonic Octo modifications

This repository contains the complete Octo 2026.09.14 source tree with
Gusonic-specific changes applied directly to the C# source.

Upstream base:

- Repository: https://github.com/winters27/octo
- Commit: 2579262a5d5ac9ed0b8a11c208e71e5059d71d95
- Release: 2026.09.14

Gusonic changes:

- Deezer public playlist search via the metadata API.
- External playlist detail and track-list retrieval.
- Playlist tracks are converted into normal Octo external placeholders.
- Playback therefore remains YouTube-based.
- Heart/acquisition remains Soulseek/Lidarr-based.
- No Deezer ARL is required for playlist discovery.
- External playlists are surfaced through Subsonic search as albums with
  genre "Playlist", matching the existing compatibility layer used by
  clients such as Arpeggi.

The upstream GPL-3.0 license is preserved.
