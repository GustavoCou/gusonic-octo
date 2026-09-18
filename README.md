# Gusonic Octo

A small, reproducible customization layer for [winters27/octo](https://github.com/winters27/octo) that adds **Deezer public playlist discovery** to the same Octo endpoint used for Navidrome, Last.fm radio, YouTube previews, Soulseek and Lidarr.

The goal is simple: **one Subsonic server URL in Arpeggi/Amperfy**.

```text
Arpeggi / Amperfy
        |
        v
   Gusonic Octo
        |
        +-- Navidrome
        +-- Deezer playlists (metadata/search)
        +-- Last.fm radio/discovery
        +-- YouTube preview
        +-- Soulseek acquisition
        +-- Lidarr
```

## What this repository changes

Octo 2026.09.14 already contains most of the external-playlist plumbing:

- `ExternalPlaylist`
- `PlaylistIdHelper`
- playlist rows merged into `search2/search3`
- `getAlbum` support for external playlist IDs
- playlist cover-art handling

However, the active `SoulseekMetadataService` returns empty/null results for the playlist provider methods.

This repository patches that missing provider layer so that:

1. `search2/search3` can discover public Deezer playlists;
2. playlist IDs use Octo's existing `pl-deezer-<id>` format;
3. opening a playlist returns its tracks;
4. each playlist track becomes a normal Octo external placeholder.

That last point keeps Octo's existing behavior intact:

```text
Deezer playlist metadata
        |
        v
Octo external track
        |
        +-- Play  -> YouTube preview
        +-- Heart -> Soulseek / Lidarr
```

**No Deezer audio is used. No Deezer ARL is required.**

## Upstream base

The build is deliberately pinned to:

```text
winters27/octo
release: 2026.09.14
commit: 2579262a5d5ac9ed0b8a11c208e71e5059d71d95
```

Pinning prevents an upstream change from silently breaking the patch.

## Build locally

```bash
git clone https://github.com/GustavoCou/gusonic-octo.git
cd gusonic-octo

cp .env.example .env
nano .env

docker compose build --no-cache octo
docker compose up -d
```

The Dockerfile runs the upstream .NET tests before publishing the image. If the patched source does not compile or the test suite fails, the image build fails.

## GHCR image

GitHub Actions builds and publishes the image to:

```text
ghcr.io/gustavocou/gusonic-octo:latest
```

After the first successful workflow run, a server can use the published image instead of building locally.

## Arpeggi

Point Arpeggi at the single Octo endpoint, for example:

```text
https://gusonic.gushub.net
```

Search for something such as:

```text
night drive
workout
chill
kizomba
```

Subsonic `search3` has no dedicated playlist result field, so external playlists are represented as albums with genre `Playlist`. Clients such as Arpeggi therefore show them near the album results.

## Reverse proxy

The public Gusonic endpoint should proxy to Octo:

```nginx
proxy_pass http://127.0.0.1:5275;
```

## Secrets

Do **not** commit your `.env`.

This repository intentionally contains no Navidrome passwords, Soulseek credentials, Last.fm keys, Lidarr API keys or Deezer account cookies.

## Relationship to upstream

This project is not affiliated with the Octo maintainers. It builds a modified version of Octo and clearly pins the upstream source revision.

Octo is GPL-3.0 licensed. This project is distributed under GPL-3.0 as well. See `LICENSE`.

The external-playlist design was also informed by [V1ck3s/octo-fiesta](https://github.com/V1ck3s/octo-fiesta), the upstream lineage documented by Octo itself.
