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
