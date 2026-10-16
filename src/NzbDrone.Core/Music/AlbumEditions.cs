using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Music
{
    // Tomvis fork (MUSIC-63): an album may monitor several releases ("editions") at once.
    // With one monitored release everything behaves as upstream.
    public static class AlbumEditions
    {
        private static readonly HashSet<string> NeutralWords = new HashSet<string> { "version", "edition", "the", "a", "an", "of", "and" };

        // Tomvis fork (MUSIC-102): words a release title uses for a later mastering, and label-name filler
        private static readonly string[] ReissueWords = { "remaster", "remastered", "remastering", "reissue", "reissued" };
        private static readonly HashSet<string> LabelNoise = new HashSet<string>
        {
            "record", "recording", "music", "musik", "entertainment", "media", "production", "distribution", "group", "label",
            "company", "co", "ltd", "inc", "llc", "corp", "corporation", "gmbh", "international", "the", "of", "on", "and", "a"
        };

        // Shorter names titles use for MusicBrainz countries
        private static readonly Dictionary<string, string[]> CountryAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Japan", new[] { "japanese", "jpn", "jp" } },
            { "United States", new[] { "usa" } },
            { "United Kingdom", new[] { "uk" } },
            { "Germany", new[] { "german" } },
            { "Europe", new[] { "eu" } }
        };

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

                // Tomvis fork (MUSIC-64): only the tracks each release wants
                if (monitored.Count <= 1)
                {
                    return monitored.Sum(WantedTracks.Count);
                }

                var imported = monitored.Where(r => importedReleaseIds.Contains(r.Id)).ToList();

                return imported.Any() ? imported.Sum(WantedTracks.Count) : monitored.Max(WantedTracks.Count);
            });
        }

        // The edition that keeps the album's normal folder: not held, titled like the album, no disambiguation, lowest id.
        public static AlbumRelease Primary(Album album, IEnumerable<AlbumRelease> monitored)
        {
            return monitored
                .OrderBy(r => r.Held)
                .ThenByDescending(r => string.Equals(r.Title, album.Title, StringComparison.OrdinalIgnoreCase))
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

        // Tomvis fork (MUSIC-102): a held edition is a candidate (a bestedition target): Lidarr searches and grabs it like
        // any missing edition, but nothing may import it without a verdict (the edition-judge plugin, MUSIC-95)
        public static bool IsHeld(Album album, AlbumRelease release)
        {
            return release?.Held == true && IsExtraEdition(album, release);
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
                // Tomvis fork (MUSIC-102): a held edition is usually the album from another mastering, told apart by its year
                var year = release.Held ? Year(release.ReleaseDate)?.ToString() : null;

                label = string.Join(" ", new[] { year, FormatMedia(release.Media), release.Country?.FirstOrDefault() }
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
        // A held edition is also matched by its release data (HeldTarget).
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
                .FirstOrDefault();

            var held = HeldTarget(album, monitored, primary, titleWords, named?.Release, named?.Words.Count ?? 0);

            if (held != null)
            {
                return held;
            }

            if (named == null)
            {
                return primary;
            }

            return named.Release.Monitored ? named.Release : null;
        }

        // Tomvis fork (MUSIC-102): a held edition is usually the album's title from another mastering, so no words set it
        // apart. Match it by the release data that tells it apart from the other monitored editions instead (year, a later
        // mastering, label, country, format): the held edition whose clues the title names most, if it names more of
        // them than of any other monitored edition's and than the words of the edition it otherwise names.
        // Nothing to tell them apart = null (the primary, as without held editions).
        private static AlbumRelease HeldTarget(Album album, List<AlbumRelease> monitored, AlbumRelease primary, HashSet<string> titleWords, AlbumRelease named, int namedWords)
        {
            if (!monitored.Any(r => r.Held && r.Id != primary?.Id))
            {
                return null;
            }

            var originalYear = Year(album.ReleaseDate) ?? monitored.Select(r => Year(r.ReleaseDate)).Min();
            var clues = monitored.ToDictionary(r => r.Id, r => Clues(r, originalYear));

            var scores = monitored
                .Select(r => new
                {
                    Release = r,
                    Score = clues[r.Id].Count(c => c.Matches(titleWords) &&
                                                   !monitored.Any(o => o.Id != r.Id && clues[o.Id].Any(x => x.Key == c.Key)))
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            var best = scores[0];

            if (best.Score == 0 || !best.Release.Held || best.Release.Id == primary?.Id ||
                scores.Skip(1).Any(x => x.Score == best.Score))
            {
                return null;
            }

            if (named != null && named.Id != best.Release.Id && namedWords >= best.Score)
            {
                return null;
            }

            return best.Release;
        }

        private static List<Clue> Clues(AlbumRelease release, int? originalYear)
        {
            var clues = new List<Clue>();
            var year = Year(release.ReleaseDate);

            if (year != null)
            {
                clues.Add(new Clue($"year:{year}", year.ToString()));
            }

            if (year != null && originalYear != null && year > originalYear)
            {
                clues.Add(new Clue("reissue", ReissueWords));
            }

            foreach (var label in release.Label ?? new List<string>())
            {
                var words = Words(label).Where(w => !LabelNoise.Contains(w)).OrderBy(w => w).ToArray();

                if (words.Any())
                {
                    clues.Add(new Clue($"label:{string.Join(" ", words)}", new List<string[]> { words }));
                }
            }

            foreach (var country in release.Country ?? new List<string>())
            {
                if (country.IsNullOrWhiteSpace() || country.StartsWith("["))
                {
                    continue;
                }

                var alternatives = new List<string[]> { Words(country).ToArray() };

                if (CountryAliases.TryGetValue(country, out var aliases))
                {
                    alternatives.AddRange(aliases.Select(a => new[] { a }));
                }

                clues.Add(new Clue($"country:{country.ToLowerInvariant()}", alternatives));
            }

            foreach (var format in (release.Media ?? new List<Medium>()).Select(m => FormatClue(m.Format)).Where(c => c != null))
            {
                clues.Add(format);
            }

            return clues.GroupBy(c => c.Key).Select(g => g.First()).ToList();
        }

        private static Clue FormatClue(string format)
        {
            var f = (format ?? string.Empty).ToLowerInvariant();

            if (f.Contains("vinyl"))
            {
                return new Clue("format:vinyl", "vinyl", "lp");
            }

            if (f.Contains("sacd"))
            {
                return new Clue("format:sacd", "sacd");
            }

            if (f.Contains("cassette"))
            {
                return new Clue("format:cassette", "cassette", "tape");
            }

            if (f.Contains("digital"))
            {
                return new Clue("format:web", "web");
            }

            if (f.Contains("cd"))
            {
                return new Clue("format:cd", "cd");
            }

            return null;
        }

        // MusicBrainz has unknown dates as DateTime.MinValue or 0001 too
        private static int? Year(DateTime? date)
        {
            return date?.Year >= 1900 ? date.Value.Year : null;
        }

        // Something release data says that a title may name: one of the alternatives, each a set of words all in the title
        private sealed class Clue
        {
            public Clue(string key, params string[] words)
                : this(key, words.Select(w => new[] { w }).ToList())
            {
            }

            public Clue(string key, List<string[]> alternatives)
            {
                Key = key;
                Alternatives = alternatives;
            }

            public string Key { get; }
            public List<string[]> Alternatives { get; }

            public bool Matches(HashSet<string> titleWords)
            {
                return Alternatives.Any(a => a.Length > 0 && a.All(titleWords.Contains));
            }
        }

        // What a grab occupies: an album, or one edition of an album with several monitored
        public static List<string> GrabKeys(RemoteAlbum remoteAlbum, string title = null)
        {
            title ??= remoteAlbum.Release?.Title;

            return remoteAlbum.Albums
                .Select(a => IsMultiEdition(a) ? $"{a.Id}:{TargetEdition(a, title)?.Id}" : $"{a.Id}")
                .ToList();
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
