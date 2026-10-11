using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    // Tomvis fork (MUSIC-64)
    [TestFixture]
    public class WantedTrackFilesFixture : CoreTest
    {
        private static List<Track> Release(params int[] wanted)
        {
            var titles = new[] { "Intro", "Song", "Другая песня", "Lux Æterna" };

            return titles.Select((t, i) => new Track
            {
                Title = t,
                MediumNumber = 1,
                AbsoluteTrackNumber = i + 1,
                AlbumReleaseId = 3,
                Monitored = wanted.Contains(i + 1)
            }).ToList();
        }

        private static List<(int Index, string Path)> Files(params string[] names)
        {
            return names.Select((n, i) => (i, n)).ToList();
        }

        [Test]
        public void whole_album_wanted_downloads_everything()
        {
            WantedTrackFiles.Select(Files("01 - Intro.flac"), Release(1, 2, 3, 4)).Should().BeNull();
        }

        [Test]
        public void matches_title_after_numbering()
        {
            var files = Files("Album/01 - Intro.flac", "Album/02. Song (2009 Remaster).flac", "Album/03 Другая песня.flac", "Album/04 - Lux Aeterna.flac", "Album/folder.jpg");

            WantedTrackFiles.Select(files, Release(2, 4)).Should().Equal(1, 3);
        }

        [Test]
        public void matches_title_in_artist_album_track_title_names()
        {
            var files = Files("Artist - Album - 01 - Intro.mp3", "Artist - Album - 02 - Song.mp3");

            WantedTrackFiles.Select(files, Release(2)).Should().Equal(1);
        }

        [Test]
        public void falls_back_to_position_when_file_count_equals_tracklist()
        {
            var files = Files("01 Intro.flac", "02 Song.flac", "03 Another Song.flac", "04 Lux.flac");

            WantedTrackFiles.Select(files, Release(3)).Should().Equal(2);
        }

        [Test]
        public void no_position_fallback_when_file_count_differs()
        {
            var files = Files("01 Intro.flac", "02 Song.flac", "03 Another Song.flac");

            WantedTrackFiles.Select(files, Release(3)).Should().BeNull();
        }

        [Test]
        public void disc_from_folder_picks_the_right_duplicate_title()
        {
            var tracks = new List<Track>
            {
                new Track { Title = "Song", MediumNumber = 1, AbsoluteTrackNumber = 1, AlbumReleaseId = 3, Monitored = false },
                new Track { Title = "Song", MediumNumber = 2, AbsoluteTrackNumber = 1, AlbumReleaseId = 3, Monitored = true }
            };
            var files = Files("Album/CD1/01 Song.flac", "Album/CD2/01 Song.flac");

            WantedTrackFiles.Select(files, tracks).Should().Equal(1);
        }

        [Test]
        public void single_image_rip_downloads_everything()
        {
            WantedTrackFiles.Select(Files("Album.flac", "Album.cue"), Release(2)).Should().BeNull();
        }

        [Test]
        public void tracks_of_several_releases_download_everything()
        {
            var tracks = Release(2);
            tracks[0].AlbumReleaseId = 4;

            WantedTrackFiles.Select(Files("02 Song.flac"), tracks).Should().BeNull();
        }

        [TestCase("Song - 2009 Remaster", "song")]
        [TestCase("Café (feat. X) [Live]", "cafe")]
        [TestCase("Lux Æterna", "luxaeterna")]
        public void normalize(string title, string expected)
        {
            WantedTrackFiles.Normalize(title).Should().Be(expected);
        }
    }
}
