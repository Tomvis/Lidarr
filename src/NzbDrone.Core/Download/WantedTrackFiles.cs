using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Download
{
    // Tomvis fork (MUSIC-64): pick the files of a multi-file download (torrent) that hold the wanted tracks
    // of a partial album. Same rules as the Tidal plugin and Tubifarry (slskd): title first, else
    // disc/track when the file count equals the tracklist. Torrents carry no durations, so none are compared.
    public static class WantedTrackFiles
    {
        private static readonly Regex LeadingNumber = new Regex(@"^\s*(?:(?<disc>\d{1,2})[-.](?<track>\d{1,2})|(?<track>\d{1,3}))(?:\s*[.)_-]\s*|\s+)", RegexOptions.Compiled);
        private static readonly Regex DiscFolder = new Regex(@"\b(?:cd|disc|disk)\s*(?<disc>\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PlainNumber = new Regex(@"^\d{1,3}$", RegexOptions.Compiled);
        private static readonly Regex SegmentSeparator = new Regex(@"\s+-\s+", RegexOptions.Compiled);
        private static readonly Regex Brackets = new Regex(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex EditionSuffix = new Regex(@"\s+-\s+[^-]*\b(remaster(ed)?|version|mix|edit|live|mono|stereo|demo|bonus)\b.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private class Candidate
        {
            public int Index { get; set; }
            public HashSet<string> Titles { get; set; }
            public int Disc { get; set; }
            public int Number { get; set; }
        }

        // File indexes to download, or null for all of them: the album wants every track, the tracks span
        // several releases, or a wanted track has no file (a partial download would leave it missing and the
        // next search would grab this release again).
        public static List<int> Select(IReadOnlyList<(int Index, string Path)> files, List<Track> releaseTracks)
        {
            if (releaseTracks == null || releaseTracks.All(t => t.Monitored) || releaseTracks.Select(t => t.AlbumReleaseId).Distinct().Count() != 1)
            {
                return null;
            }

            var audio = files.Where(f => IsAudio(f.Path)).Select(f => ToCandidate(f.Index, f.Path)).ToList();
            var picked = new List<int>();

            foreach (var track in releaseTracks.Where(t => t.Monitored))
            {
                var match = ByTitle(audio, track) ?? ByPosition(audio, track, releaseTracks.Count);

                if (match == null)
                {
                    return null;
                }

                if (!picked.Contains(match.Index))
                {
                    picked.Add(match.Index);
                }
            }

            return picked;
        }

        private static Candidate ByTitle(List<Candidate> audio, Track track)
        {
            var title = Normalize(track.Title);

            if (title.Length == 0)
            {
                return null;
            }

            var candidates = audio.Where(c => c.Titles.Contains(title)).ToList();

            return candidates.FirstOrDefault(c => c.Disc == track.MediumNumber && c.Number == track.AbsoluteTrackNumber)
                ?? (candidates.Count == 1 ? candidates[0] : null);
        }

        private static Candidate ByPosition(List<Candidate> audio, Track track, int releaseTrackCount)
        {
            if (audio.Count != releaseTrackCount)
            {
                return null;
            }

            return audio.FirstOrDefault(c => c.Disc == track.MediumNumber && c.Number == track.AbsoluteTrackNumber);
        }

        private static bool IsAudio(string path)
        {
            return MediaFileExtensions.Extensions.Contains(Path.GetExtension(path ?? ""));
        }

        // "Artist - Album - 06 - Title.flac": every " - " suffix is a title candidate ("Title", "06 - Title", ...)
        // after the leading "06 ", "06.", "1-06 " or "106 " numbering is stripped.
        private static Candidate ToCandidate(int index, string filePath)
        {
            var path = (filePath ?? "").Replace('\\', '/');
            var stem = Path.GetFileNameWithoutExtension(path);
            var parts = SegmentSeparator.Split(stem);
            var disc = 0;
            var number = 0;

            // Numbering leads the name ("06 Title") or is its own segment ("Artist - Album - 06 - Title")
            var numbering = LeadingNumber.Match(stem);
            var segment = parts.FirstOrDefault(p => PlainNumber.IsMatch(p));

            if (!numbering.Success && segment != null)
            {
                numbering = LeadingNumber.Match(segment + " ");
            }

            if (numbering.Success)
            {
                number = int.Parse(numbering.Groups["track"].Value);

                if (numbering.Groups["disc"].Success)
                {
                    disc = int.Parse(numbering.Groups["disc"].Value);
                }
                else if (numbering.Groups["track"].Value.Length == 3)
                {
                    disc = number / 100;
                    number %= 100;
                }
            }

            if (disc == 0)
            {
                var parent = DiscFolder.Match(Path.GetFileName(Path.GetDirectoryName(path) ?? ""));
                disc = parent.Success ? int.Parse(parent.Groups["disc"].Value) : 1;
            }

            var titles = new HashSet<string>();

            for (var i = 0; i < parts.Length; i++)
            {
                var title = Normalize(string.Join(" - ", parts.Skip(i)));

                if (title.Length > 0)
                {
                    titles.Add(title);
                }

                var stripped = LeadingNumber.Replace(parts[i], "", 1);
                var rest = Normalize(string.Join(" - ", new[] { stripped }.Concat(parts.Skip(i + 1))));

                if (stripped != parts[i] && rest.Length > 0)
                {
                    titles.Add(rest);
                }
            }

            return new Candidate { Index = index, Titles = titles, Disc = disc, Number = number };
        }

        // "Song (feat. X) [2009 Remaster]" and "Song - 2009 Remaster" both become "song"; "Lux Æterna" = "Lux Aeterna"
        public static string Normalize(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var t = Brackets.Replace(title, " ");
            t = EditionSuffix.Replace(t, " ");
            t = t.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder();

            foreach (var c in t)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }

                var l = char.ToLowerInvariant(c);
                sb.Append(l switch
                {
                    'æ' => "ae",
                    'œ' => "oe",
                    'ø' => "o",
                    'ß' => "ss",
                    'ł' => "l",
                    'đ' => "d",
                    _ => l.ToString()
                });
            }

            return sb.ToString();
        }
    }
}
