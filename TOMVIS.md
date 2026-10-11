# Tomvis/Lidarr fork

Branch `enhanced` = upstream `develop` tag + fork commits. forksync (.120) rebases it nightly; see homelab-stacks `forks.toml`.

## Database migrations

- Fork migrations take versions **1000+** and class names `tomvis_*` (file `1000_tomvis_<name>.cs`), in namespace `NzbDrone.Core.Datastore.Migration` (the runner does not scan sub-namespaces). `TomvisMigrationRangeFixture` enforces this.
- Upstream keeps numbering from 0xx, so versions never collide. FluentMigrator applies any unapplied version, so an upstream 0xx that lands after a fork 1000 still runs: keep fork migrations additive (new tables/columns) so upstream ones still apply on top.
- Before deploying a new migration: `pg_dump -Fc lidarr-main` (forward-only).

## Webhook additions

- `album.albumRelease` on Grab (the monitored release, i.e. Lidarr's import target) and on Download/upgrade (the release the files were imported as): `id`, `mbId`, `title`, `disambiguation`, `country`, `format` (`2xCD`), `trackCount`, `status`, `releaseDate`. Omitted when unknown.

## Editions (MUSIC-63)

- An album may monitor several releases ("editions") at once: `PUT /api/v1/album` with `monitored: true` on each wanted release. One monitored release = upstream behaviour everywhere. Helpers: `NzbDrone.Core/Music/AlbumEditions.cs`.
- Primary edition = monitored release titled like the album, no disambiguation, lowest id; it keeps the album folder. An extra edition's `{Album Title}` tokens become its edition title (`Ihsahn (orchestral version)`, else album title + disambiguation, else format + country), so the naming format must use an album-title token or editions share a folder.
- Refresh keeps every monitored edition and never monitors new MB releases. An import replaces/upgrades only the edition identification picked (not pinned to the grab: a mislabeled grab must not import one edition as another). Removing an edition unlinks only its tracks.
- Search: a result is matched to an edition by the words that set it apart from the album (`orchestral`, `instrumentals`); no such words = primary. Cutoff/upgrade checks use that edition's files; a result naming an unmonitored edition is rejected (`EditionSpecification`). A result titled as a monitored edition (`Ihsahn (Orchestral Version)`) matches an album search. Grabs and the queue check are per (album, edition), so one search can grab every missing edition. The Grab webhook's `albumRelease` is the matched edition.
- Not done: UI multi-select (the image ships hotio's frontend, so use the API), plugin indexers passing an exact release id.

## Rescans keep download provenance (MUSIC-68)

- A rescan that reuses a TrackFile row (MUSIC-66) keeps its `SceneName`, `OriginalFilePath`, and its `ReleaseGroup`/`IndexerFlags` when the rescan finds none. Upstream rebuilt the row from disk alone, so every beets retag wiped the release name; custom formats then scored the bare filename and searches approved CF-only "upgrades" of the same release.
- 2026-10-10 one-off backfill: 20,633/24,472 rows got `SceneName` from the grab their track's latest import came from (History `TrackFileImported.DownloadId` → `Grabbed`/`DownloadImported` title). Pre-backfill dump: `/mnt/Fast/backups/postgres/adhoc-music68-20261010-2019/`.

## Wanted tracks (MUSIC-64, phase 1: core)

- `Tracks.Monitored` (migration 1000, default true): an album can want only some tracks of its monitored release. All wanted = upstream behaviour. Helpers: `NzbDrone.Core/Music/WantedTracks.cs`.
- API: `PUT /api/v1/track/monitor {trackIds, monitored}`; `TrackResource.monitored`. Wanting a track of an unmonitored album monitors the album for that track only; unwanting its last wanted track unmonitors the album and resets its tracks to wanted. Tracks must be on the album's monitored release (400 otherwise).
- Missing/cutoff/stats, `TracksWithoutFiles`, upgrade and cutoff specs count only wanted tracks and their files.
- Import of a new download: unwanted files are rejected (`TrackWantedSpecification`); missing unwanted tracks neither reject (`NoMissingOrUnmatchedTracksSpecification`) nor add distance; another release can't stand in (`ReleaseWantedSpecification`, even with `anyReleaseOk`); the album's other files are not recycled (only replaced tracks are); completion = every wanted track imported.
- Refresh keeps the flag: merges keep it, MusicBrainz-added tracks of a partial release start unwanted, and a monitored-release switch carries wanted recordings over (`TrackService.CarryOverMonitoring`).
- Search: `AlbumSearchCriteria.Tracks` = wanted tracks without files when the album is partial (null otherwise) for indexer plugins.
- Webhooks: tracks carry `mbId`, `recordingMbId`, `mediumNumber`, `absoluteTrackNumber`; `album.albumRelease.wantedTracks` lists the wanted tracks of a partial release (null when all are wanted).
- Tidal plugin (Tomvis/Lidarr.Plugin.Tidal `1eb42be`, phase 2a): a grab for a partial album downloads only the wanted tracks (title+duration, else disc/track); an unmatched wanted track = whole album. Reads `Track.Monitored` by reflection (still builds on upstream). beets-lidarr imports a partial album with `missing_tracks` not counted.
- Not done yet: slskd, YouTube Music, qBittorrent file priorities, UI.
