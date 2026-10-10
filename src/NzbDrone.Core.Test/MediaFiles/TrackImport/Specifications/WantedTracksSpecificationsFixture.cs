using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Specifications
{
    // Tomvis fork (MUSIC-64): an album that wants only some of its tracks
    public abstract class WantedTracksFixture<TSubject> : CoreTest<TSubject>
        where TSubject : class
    {
        protected Album Album;
        protected AlbumRelease Monitored;
        protected AlbumRelease Other;

        [SetUp]
        public void SetupAlbum()
        {
            Album = Builder<Album>.CreateNew()
                .With(a => a.AnyReleaseOk = true)
                .Build();

            Monitored = Release(1, true, Tracks(1, 3));
            Other = Release(2, false, Tracks(2, 3));

            Album.AlbumReleases = new List<AlbumRelease> { Monitored, Other };
        }

        protected static List<Track> Tracks(int releaseId, int count)
        {
            return Builder<Track>.CreateListOfSize(count)
                .All()
                .With(t => t.AlbumReleaseId = releaseId)
                .With(t => t.Monitored = true)
                .Build()
                .ToList();
        }

        protected void WantOnlyFirstTrack()
        {
            Monitored.Tracks.Value.Skip(1).ToList().ForEach(t => t.Monitored = false);
        }

        private AlbumRelease Release(int id, bool monitored, List<Track> tracks)
        {
            return Builder<AlbumRelease>.CreateNew()
                .With(r => r.Id = id)
                .With(r => r.Monitored = monitored)
                .With(r => r.Album = Album)
                .With(r => r.TrackCount = tracks.Count)
                .With(r => r.Tracks = tracks)
                .Build();
        }

        protected LocalAlbumRelease LocalRelease(AlbumRelease release, List<Track> missing)
        {
            return new LocalAlbumRelease
            {
                AlbumRelease = release,
                NewDownload = true,
                TrackMapping = new TrackMapping { LocalExtra = new List<LocalTrack>(), MBExtra = missing }
            };
        }
    }

    [TestFixture]
    public class NoMissingWantedTracksFixture : WantedTracksFixture<NoMissingOrUnmatchedTracksSpecification>
    {
        [Test]
        public void should_accept_new_download_missing_only_unwanted_tracks()
        {
            WantOnlyFirstTrack();

            Subject.IsSatisfiedBy(LocalRelease(Monitored, Monitored.Tracks.Value.Skip(1).ToList()), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_new_download_missing_a_wanted_track()
        {
            WantOnlyFirstTrack();

            Subject.IsSatisfiedBy(LocalRelease(Monitored, Monitored.Tracks.Value.ToList()), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class ReleaseWantedForWantedTracksFixture : WantedTracksFixture<ReleaseWantedSpecification>
    {
        [Test]
        public void should_accept_other_release_when_album_wants_every_track()
        {
            Subject.IsSatisfiedBy(LocalRelease(Other, new List<Track>()), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_other_release_when_album_wants_only_some_tracks()
        {
            WantOnlyFirstTrack();

            Subject.IsSatisfiedBy(LocalRelease(Other, new List<Track>()), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_monitored_release_when_album_wants_only_some_tracks()
        {
            WantOnlyFirstTrack();

            Subject.IsSatisfiedBy(LocalRelease(Monitored, new List<Track>()), null).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class TrackWantedSpecificationFixture : WantedTracksFixture<TrackWantedSpecification>
    {
        private LocalTrack LocalTrack(bool existingFile, params Track[] tracks)
        {
            return new LocalTrack { ExistingFile = existingFile, Tracks = tracks.ToList(), Path = "/download/track.flac" };
        }

        [Test]
        public void should_reject_downloaded_file_of_unwanted_track()
        {
            WantOnlyFirstTrack();

            Subject.IsSatisfiedBy(LocalTrack(false, Monitored.Tracks.Value[1]), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_downloaded_file_of_wanted_track()
        {
            WantOnlyFirstTrack();

            Subject.IsSatisfiedBy(LocalTrack(false, Monitored.Tracks.Value[0]), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_existing_file_of_unwanted_track()
        {
            WantOnlyFirstTrack();

            Subject.IsSatisfiedBy(LocalTrack(true, Monitored.Tracks.Value[1]), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_unmatched_file()
        {
            Subject.IsSatisfiedBy(LocalTrack(false), null).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class WantedTrackCountFixture : WantedTracksFixture<NoMissingOrUnmatchedTracksSpecification>
    {
        [Test]
        public void download_should_be_expected_to_bring_every_track_when_album_wants_them_all()
        {
            AlbumEditions.ExpectedTrackCount(new[] { Album }, new List<Track>()).Should().Be(3);
        }

        [Test]
        public void download_should_be_expected_to_bring_only_wanted_tracks()
        {
            WantOnlyFirstTrack();

            AlbumEditions.ExpectedTrackCount(new[] { Album }, new List<Track>()).Should().Be(1);
        }
    }
}
