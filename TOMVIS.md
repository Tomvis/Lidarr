# Tomvis/Lidarr fork

Branch `enhanced` = upstream `develop` tag + fork commits. forksync (.120) rebases it nightly; see homelab-stacks `forks.toml`.

## Database migrations

- Fork migrations take versions **1000+** and class names `tomvis_*` (file `1000_tomvis_<name>.cs`), in namespace `NzbDrone.Core.Datastore.Migration` (the runner does not scan sub-namespaces). `TomvisMigrationRangeFixture` enforces this.
- Upstream keeps numbering from 0xx, so versions never collide. FluentMigrator applies any unapplied version, so an upstream 0xx that lands after a fork 1000 still runs: keep fork migrations additive (new tables/columns) so upstream ones still apply on top.
- Before deploying a new migration: `pg_dump -Fc lidarr-main` (forward-only).

## Webhook additions

- `album.albumRelease` on Grab (the monitored release, i.e. Lidarr's import target) and on Download/upgrade (the release the files were imported as): `id`, `mbId`, `title`, `disambiguation`, `country`, `format` (`2xCD`), `trackCount`, `status`, `releaseDate`. Omitted when unknown.
