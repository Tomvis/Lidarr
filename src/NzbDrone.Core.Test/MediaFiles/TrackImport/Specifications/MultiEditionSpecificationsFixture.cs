using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Specifications
{
    // Tomvis fork (MUSIC-63): several monitored releases of one album
    public abstract class MultiEditionSpecificationFixture<TSubject> : CoreTest<TSubject>
        where TSubject : class
    {
        protected Album Album;
        protected AlbumRelease Main;
        protected AlbumRelease Extra;
        protected AlbumRelease Unmonitored;

        [SetUp]
        public void SetupEditions()
        {
            var artist = Builder<Artist>.CreateNew()
                .With(a => a.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .Build();

            Album = Builder<Album>.CreateNew()
                .With(a => a.AnyReleaseOk = true)
                .With(a => a.Artist = artist)
                .Build();

            var mainFile = new TrackFile { Id = 100, Quality = new QualityModel(Quality.FLAC) };
            var mainTracks = Builder<Track>.CreateListOfSize(10)
                .All()
                .With(t => t.TrackFileId = mainFile.Id)
                .With(t => t.TrackFile = mainFile)
                .Build()
                .ToList();

            Main = Release(1, true, mainTracks);
            Extra = Release(2, true, new List<Track>());
            Unmonitored = Release(3, false, new List<Track>());

            Album.AlbumReleases = new List<AlbumRelease> { Main, Extra, Unmonitored };
        }

        private AlbumRelease Release(int id, bool monitored, List<Track> tracks)
        {
            return Builder<AlbumRelease>.CreateNew()
                .With(r => r.Id = id)
                .With(r => r.Monitored = monitored)
                .With(r => r.Album = Album)
                .With(r => r.Tracks = tracks)
                .Build();
        }

        protected void GivenSingleEdition()
        {
            Extra.Monitored = false;
        }

        protected LocalAlbumRelease Importing(AlbumRelease release, int tracks = 5)
        {
            var localTracks = Builder<LocalTrack>.CreateListOfSize(tracks)
                .All()
                .With(t => t.Quality = new QualityModel(Quality.MP3_320))
                .Build()
                .ToList();

            return new LocalAlbumRelease(localTracks) { AlbumRelease = release };
        }
    }

    [TestFixture]
    public class ReleaseWantedMultiEditionFixture : MultiEditionSpecificationFixture<ReleaseWantedSpecification>
    {
        [Test]
        public void should_accept_any_release_when_one_is_monitored_and_any_release_ok()
        {
            GivenSingleEdition();

            Subject.IsSatisfiedBy(Importing(Unmonitored), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_monitored_edition()
        {
            Subject.IsSatisfiedBy(Importing(Extra), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_unmonitored_release_when_several_are_monitored_even_if_any_release_ok()
        {
            Subject.IsSatisfiedBy(Importing(Unmonitored), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class AlbumUpgradeMultiEditionFixture : MultiEditionSpecificationFixture<AlbumUpgradeSpecification>
    {
        [Test]
        public void should_not_compare_an_edition_against_another_edition()
        {
            Subject.IsSatisfiedBy(Importing(Extra), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_still_reject_a_release_switch_that_downgrades_when_one_is_monitored()
        {
            GivenSingleEdition();

            Subject.IsSatisfiedBy(Importing(Unmonitored), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class MoreTracksMultiEditionFixture : MultiEditionSpecificationFixture<MoreTracksSpecification>
    {
        [Test]
        public void should_not_compare_track_counts_across_editions()
        {
            Subject.IsSatisfiedBy(Importing(Extra, 5), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_still_reject_a_release_switch_with_fewer_tracks_when_one_is_monitored()
        {
            GivenSingleEdition();

            Subject.IsSatisfiedBy(Importing(Unmonitored, 5), null).Accepted.Should().BeFalse();
        }
    }
}
