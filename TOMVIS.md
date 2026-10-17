# Tomvis/Lidarr fork

Branch `enhanced` = upstream `develop` tag + fork commits. The image (`docker/Dockerfile.tomvis`) overlays the fork's backend and UI on hotio's nightly. forksync (.120) rebases it nightly; see homelab-stacks `forks.toml`.

## Database migrations

- Fork migrations take versions **1000+** and class names `tomvis_*` (file `1000_tomvis_<name>.cs`), in namespace `NzbDrone.Core.Datastore.Migration` (the runner does not scan sub-namespaces). `TomvisMigrationRangeFixture` enforces this.
- Upstream keeps numbering from 0xx, so versions never collide. FluentMigrator applies any unapplied version, so an upstream 0xx that lands after a fork 1000 still runs: keep fork migrations additive (new tables/columns) so upstream ones still apply on top.
- Before deploying a new migration: `pg_dump -Fc lidarr-main` (forward-only).

## Webhook additions

- Download fires for a ManualImport of files from outside the artist folder (MUSIC-97). Upstream fired it only with `replaceExistingFiles`, so force-imports and inbox moves were silent.
- `album.albumRelease` on Grab (the monitored release, i.e. Lidarr's import target) and on Download/upgrade (the release the files were imported as): `id`, `mbId`, `title`, `disambiguation`, `country`, `format` (`2xCD`), `trackCount`, `status`, `releaseDate`. Omitted when unknown.

## Editions (MUSIC-63)

- An album may monitor several releases ("editions") at once: `PUT /api/v1/album` with `monitored: true` on each wanted release. One monitored release = upstream behaviour everywhere. Helpers: `NzbDrone.Core/Music/AlbumEditions.cs`.
- Primary edition = monitored release titled like the album, no disambiguation, lowest id; it keeps the album folder. An extra edition's `{Album Title}` tokens become its edition title (`Ihsahn (orchestral version)`, else album title + disambiguation, else format + country), so the naming format must use an album-title token or editions share a folder.
- Refresh keeps every monitored edition and never monitors new MB releases. An import replaces/upgrades only the edition identification picked (not pinned to the grab: a mislabeled grab must not import one edition as another). Removing an edition unlinks only its tracks.
- Search: a result is matched to an edition by the words that set it apart from the album (`orchestral`, `instrumentals`); no such words = primary. Cutoff/upgrade checks use that edition's files; a result naming an unmonitored edition is rejected (`EditionSpecification`). A result titled as a monitored edition (`Ihsahn (Orchestral Version)`) matches an album search. Grabs and the queue check are per (album, edition), so one search can grab every missing edition. The Grab webhook's `albumRelease` is the matched edition.
- UI: Edit Album's release picker is a multi-select of monitored editions (MUSIC-68); it refuses to empty the list. Upstream's single select unmonitored every other edition on change.
- Album page: with several monitored editions, one track section per edition medium, headed by the edition title (`track.albumReleaseId` in the API). Upstream grouped by medium number only, so editions' medium 1 merged.
- Plugin indexers: Tubifarry (Soulseek) fits a folder to each monitored edition (file count, then closest durations) and adds an extra edition's title to the result title, since a matched folder is otherwise titled after the album (MUSIC-68). Tidal needs nothing: its titles carry Tidal's version ("Ihsahn (Orchestral Version)").

## Held editions (MUSIC-102)

- `AlbumRelease.Held` (migration 1001): a monitored extra edition that is only a candidate (a bestedition DR target). Set via `PUT /api/v1/album` (`releases[].held`); in the API and in webhooks' `albumRelease.held`. `AlbumEditions.IsHeld` = held, monitored and not the primary; nothing imports it on its own (below).
- A held edition is never the primary (folder, default match), and its edition title names its year (`Elvis Presley (1999 CD United States)`).
- Search: a held edition is usually the album's title from another mastering, so the words matcher can't see it. It is also matched by release data: year, a later mastering (`remaster`/`reissue`, when its year is after the album's), label, country (`Japan`/`japanese`/`jp`, `usa`, `uk`; bare `US` is too common a word), format (`vinyl`/`lp`, `web`, `cd`, `sacd`). Only clues no other monitored edition shares count; the held edition wins if the title names more of its clues than any other monitored edition's and than the words of a release it otherwise names. A tie or nothing = the primary, as without held editions. Held editions show as missing, so Wanted/Missing, RSS and decluttarr search them.
- Not in core: catalog numbers (Lidarr doesn't store them), DR, the verdict. Tubifarry (slskd) still fits folders by durations, so same-title held editions mostly land on the primary there.
- `PUT /api/v1/album` now sets only `monitored`/`held` on existing releases. Upstream wrote the resource's releases whole, and the resource has no `ReleaseDate` or `OldForeignReleaseIds`, so every album edit nulled them (every multi-edition album in prod had no release dates); an album refresh refills them.
- Never auto-imported (MUSIC-95, `HeldEditionSpecification`): a new download is rejected ("awaiting edition judge") when identification picks a held edition, or when its grab title (Grabbed history, else the client's title) matches one, whatever identification picked: a same-tracklist mastering often identifies as the owned edition. It waits in the queue, import-blocked. The judge's pass = `ManualImport` pinned to the held `albumReleaseId` (ManualImport skips import specs); fail = `DELETE /queue/{id}?blocklist=true&removeFromClient=true`. Verdicts live in the pipeline, not Lidarr. Files already in the library are not checked.
- While an album holds an edition, a result for any other edition is rejected unless that edition misses tracks (MUSIC-103, `HeldAlbumSpecification`). A held edition's searches also find copies of the owned edition, and those took custom-format upgrades over the owned files without the judge.

## Rescans keep download provenance (MUSIC-68)

- A rescan that reuses a TrackFile row (MUSIC-66) keeps its `SceneName`, `OriginalFilePath`, and its `ReleaseGroup`/`IndexerFlags` when the rescan finds none. Upstream rebuilt the row from disk alone, so every beets retag wiped the release name; custom formats then scored the bare filename and searches approved CF-only "upgrades" of the same release.
- 2026-10-10 one-off backfill: 20,633/24,472 rows got `SceneName` from the grab their track's latest import came from (History `TrackFileImported.DownloadId` → `Grabbed`/`DownloadImported` title). Pre-backfill dump: `/mnt/Fast/backups/postgres/adhoc-music68-20261010-2019/`.

## Imports keep tracks they don't bring (MUSIC-96)

- Upstream's import of a download (`replaceExisting`) recycled every file of the album first, so a grab missing some tracks lost them. Now a file on a track of the imported release stays; `UpgradeTrackFile` replaces it only if the import brings that track.
- After a release switch, a file whose recording the new release has but the import doesn't bring moves to that track (`SetFileIds`). Files whose recording the import brings, the new release lacks, or that sit on no track are recycled as upstream.
- Backstop: beets-lidarr `lidarr_recycle_guard.py` (TrueNAS cron 100) goes report-only after this ships, then retires.

## Wanted tracks (MUSIC-64, phase 1: core)

- `Tracks.Monitored` (migration 1000, default true): an album can want only some tracks of its monitored release. All wanted = upstream behaviour. Helpers: `NzbDrone.Core/Music/WantedTracks.cs`.
- API: `PUT /api/v1/track/monitor {trackIds, monitored}`; `TrackResource.monitored`. Wanting a track of an unmonitored album monitors the album for that track only; unwanting its last wanted track unmonitors the album and resets its tracks to wanted. Tracks must be on the album's monitored release (400 otherwise).
- Missing/cutoff/stats, `TracksWithoutFiles`, upgrade and cutoff specs count only wanted tracks and their files.
- Import of a new download: unwanted files are rejected (`TrackWantedSpecification`); missing unwanted tracks neither reject (`NoMissingOrUnmatchedTracksSpecification`) nor add distance; another release can't stand in (`ReleaseWantedSpecification`, even with `anyReleaseOk`); the album's other files are not recycled (only replaced tracks are); completion = every wanted track imported.
- Refresh keeps the flag: merges keep it, MusicBrainz-added tracks of a partial release start unwanted, and a monitored-release switch carries wanted recordings over (`TrackService.CarryOverMonitoring`).
- Search: `AlbumSearchCriteria.Tracks` = wanted tracks without files when the album is partial (null otherwise) for indexer plugins.
- Webhooks: tracks carry `mbId`, `recordingMbId`, `mediumNumber`, `absoluteTrackNumber`; `album.albumRelease.wantedTracks` lists the wanted tracks of a partial release (null when all are wanted).
- Tidal plugin (Tomvis/Lidarr.Plugin.Tidal `1eb42be`, phase 2a): a grab for a partial album downloads only the wanted tracks (title+duration, else disc/track); an unmatched wanted track = whole album. Reads `Track.Monitored` by reflection (still builds on upstream). beets-lidarr imports a partial album with `missing_tracks` not counted.
- qBittorrent (phase 2c): a grab for a partial album is added tagged `lidarr-wanted-tracks-<albumId>` with `stopCondition=MetadataReceived` (API 2.9+), so it stops once the file list is in. The next queue check sets priority 0 on every file but the wanted tracks' (`Download/WantedTrackFiles.cs`: title, else disc/track when the audio-file count equals the tracklist; no match for a wanted track = whole torrent), starts it per the client's initial state, then drops the tag.
- slskd: Tubifarry fork (phase 2b). YouTube Music: Tubifarry fork (phase 2d).
- UI (phase 3): a wanted toggle per track on the album page; medium counts show wanted tracks. A track flag change publishes `AlbumEditedEvent` (stats + SignalR push).
