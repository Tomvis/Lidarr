using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Music
{
    // Tomvis fork (MUSIC-63): an album may monitor several releases ("editions") at once.
    // With one monitored release everything behaves as upstream.
    public static class AlbumEditions
    {
        public static List<AlbumRelease> Monitored(IEnumerable<AlbumRelease> releases)
        {
            return releases?.Where(r => r.Monitored).ToList() ?? new List<AlbumRelease>();
        }

        public static bool IsMultiEdition(Album album)
        {
            return Monitored(album?.AlbumReleases?.Value).Count > 1;
        }

        // The monitored release an import replaces or upgrades: itself when monitored, else the single monitored one.
        public static AlbumRelease Current(Album album, AlbumRelease release)
        {
            var monitored = Monitored(album.AlbumReleases.Value);

            return monitored.FirstOrDefault(r => r.Id == release.Id) ?? Primary(album, monitored);
        }

        // Tracks a download of these albums should bring: every monitored release with one per album (upstream),
        // only the imported editions with several (a download carries one edition, not all of them).
        public static int ExpectedTrackCount(IEnumerable<Album> albums, IEnumerable<Track> importedTracks)
        {
            var importedReleaseIds = importedTracks.Select(t => t.AlbumReleaseId).ToHashSet();

            return albums.Sum(album =>
            {
                var monitored = Monitored(album.AlbumReleases.Value);

                if (monitored.Count <= 1)
                {
                    return monitored.Sum(r => r.TrackCount);
                }

                var imported = monitored.Where(r => importedReleaseIds.Contains(r.Id)).ToList();

                return imported.Any() ? imported.Sum(r => r.TrackCount) : monitored.Max(r => r.TrackCount);
            });
        }

        // The edition that keeps the album's normal folder: titled like the album, no disambiguation, lowest id.
        public static AlbumRelease Primary(Album album, IEnumerable<AlbumRelease> monitored)
        {
            return monitored
                .OrderByDescending(r => string.Equals(r.Title, album.Title, StringComparison.OrdinalIgnoreCase))
                .ThenBy(r => r.Disambiguation.IsNotNullOrWhiteSpace())
                .ThenBy(r => r.Id)
                .FirstOrDefault();
        }

        public static bool IsExtraEdition(Album album, AlbumRelease release)
        {
            if (album == null || release == null)
            {
                return false;
            }

            var monitored = Monitored(album.AlbumReleases?.Value);

            return monitored.Count > 1 &&
                   monitored.Any(r => r.Id == release.Id) &&
                   Primary(album, monitored).Id != release.Id;
        }

        // An extra edition's title for naming: its own title when it differs from the album's
        // ("Ihsahn (orchestral version)"), else the album title qualified by disambiguation, or format and country.
        public static string EditionTitle(Album album, AlbumRelease release)
        {
            if (release.Title.IsNotNullOrWhiteSpace() &&
                !string.Equals(release.Title, album.Title, StringComparison.OrdinalIgnoreCase))
            {
                return release.Title;
            }

            var label = release.Disambiguation;

            if (label.IsNullOrWhiteSpace())
            {
                label = string.Join(" ", new[] { FormatMedia(release.Media), release.Country?.FirstOrDefault() }
                    .Where(x => x.IsNotNullOrWhiteSpace()));
            }

            if (label.IsNullOrWhiteSpace())
            {
                label = release.ForeignReleaseId;
            }

            return $"{album.Title} ({label})";
        }

        // MusicBrainz style: "2xCD", "CD + DVD"
        public static string FormatMedia(List<Medium> media)
        {
            if (media == null || media.Count == 0)
            {
                return null;
            }

            return string.Join(" + ", media.OrderBy(m => m.Number)
                .GroupBy(m => m.Format)
                .Select(g => g.Count() > 1 ? $"{g.Count()}x{g.Key}" : g.Key));
        }
    }
}
