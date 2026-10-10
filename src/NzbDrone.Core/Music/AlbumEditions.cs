using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Music
{
    // Tomvis fork (MUSIC-63): an album may monitor several releases ("editions") at once.
    // With one monitored release everything behaves as upstream.
    public static class AlbumEditions
    {
        private static readonly HashSet<string> NeutralWords = new HashSet<string> { "version", "edition", "the", "a", "an", "of", "and" };

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

        // The edition a search result (or grab) is for, by the words that set an edition apart from the album
        // ("orchestral", "instrumentals"): a release whose words all appear in the title (most words wins,
        // monitored first), else the primary edition. Null when the title names an unmonitored edition.
        public static AlbumRelease TargetEdition(Album album, string title)
        {
            var monitored = Monitored(album.AlbumReleases.Value);
            var primary = Primary(album, monitored);
            var titleWords = Words(title);

            var named = album.AlbumReleases.Value
                .Where(r => primary == null || r.Id != primary.Id)
                .Select(r => new { Release = r, Words = EditionWords(album, r) })
                .Where(x => x.Words.Any() && x.Words.All(titleWords.Contains))
                .OrderByDescending(x => x.Words.Count)
                .ThenByDescending(x => x.Release.Monitored)
                .Select(x => x.Release)
                .FirstOrDefault();

            if (named == null)
            {
                return primary;
            }

            return named.Monitored ? named : null;
        }

        private static List<string> EditionWords(Album album, AlbumRelease release)
        {
            var albumWords = Words(album.Title);

            return Words($"{release.Title} {release.Disambiguation}")
                .Where(w => !albumWords.Contains(w) && !NeutralWords.Contains(w))
                .ToList();
        }

        private static HashSet<string> Words(string text)
        {
            return Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^\p{L}\p{N}]+")
                .Where(w => w.Length > 0)
                .Select(w => w.Length > 3 && w.EndsWith("s") ? w.Substring(0, w.Length - 1) : w)
                .ToHashSet();
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
