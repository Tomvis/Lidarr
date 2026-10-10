using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    [TestFixture]
    public class WebhookAlbumReleaseFixture : CoreTest<Webhook>
    {
        private Artist _artist;
        private Album _album;
        private AlbumRelease _main;
        private AlbumRelease _deluxe;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<ITagRepository>()
                  .Setup(s => s.GetTags(It.IsAny<HashSet<int>>()))
                  .Returns(new List<Tag>());

            _artist = new Artist
            {
                Id = 1,
                Metadata = new ArtistMetadata { ForeignArtistId = "artist-mbid", Images = new List<MediaCover.MediaCover>() },
                Tags = new HashSet<int>()
            };

            _main = new AlbumRelease
            {
                Id = 10,
                ForeignReleaseId = "main-mbid",
                Title = "Album",
                Disambiguation = "",
                Country = new List<string> { "Norway" },
                Media = new List<Medium> { new Medium { Number = 1, Format = "Digital Media" } },
                TrackCount = 11,
                Status = "Official",
                Monitored = true
            };

            _deluxe = new AlbumRelease
            {
                Id = 11,
                ForeignReleaseId = "deluxe-mbid",
                Title = "Album",
                Disambiguation = "orchestral",
                Media = new List<Medium> { new Medium { Number = 1, Format = "CD" }, new Medium { Number = 2, Format = "CD" } },
                TrackCount = 22,
                Monitored = false
            };

            _album = new Album
            {
                Id = 5,
                ForeignAlbumId = "rg-mbid",
                Title = "Album",
                AlbumReleases = new LazyLoaded<List<AlbumRelease>>(new List<AlbumRelease> { _deluxe, _main })
            };
        }

        private GrabMessage GrabMessage()
        {
            return new GrabMessage
            {
                Artist = _artist,
                Quality = new QualityModel(Quality.FLAC),
                RemoteAlbum = new RemoteAlbum
                {
                    Albums = new List<Album> { _album },
                    ParsedAlbumInfo = new ParsedAlbumInfo(),
                    Release = new ReleaseInfo { Title = "Artist - Album [FLAC]" }
                }
            };
        }

        private AlbumDownloadMessage ImportMessage(AlbumRelease release, bool upgrade)
        {
            return new AlbumDownloadMessage
            {
                Artist = _artist,
                Album = _album,
                Release = release,
                TrackFiles = new List<TrackFile>(),
                OldFiles = upgrade ? new List<TrackFile> { new TrackFile { Path = "/old.flac", Quality = new QualityModel(Quality.MP3_320), Tracks = new List<Track>() } } : new List<TrackFile>()
            };
        }

        [Test]
        public void grab_should_carry_the_monitored_release()
        {
            var payload = Subject.BuildOnGrabPayload(GrabMessage());

            var release = payload.Albums[0].AlbumRelease;
            release.Should().NotBeNull();
            release.Id.Should().Be(10);
            release.MBId.Should().Be("main-mbid");
            release.Country.Should().Equal("Norway");
            release.Format.Should().Be("Digital Media");
            release.TrackCount.Should().Be(11);
            release.Status.Should().Be("Official");
        }

        [Test]
        public void grab_should_omit_release_when_none_is_monitored()
        {
            _main.Monitored = false;

            var payload = Subject.BuildOnGrabPayload(GrabMessage());

            payload.Albums[0].AlbumRelease.Should().BeNull();
        }

        [Test]
        public void import_should_carry_the_imported_release_not_the_monitored_one()
        {
            var payload = Subject.BuildOnReleaseImportPayload(ImportMessage(_deluxe, false));

            payload.Album.AlbumRelease.MBId.Should().Be("deluxe-mbid");
            payload.Album.AlbumRelease.Disambiguation.Should().Be("orchestral");
            payload.Album.AlbumRelease.Format.Should().Be("2xCD");
            payload.IsUpgrade.Should().BeFalse();
        }

        [Test]
        public void upgrade_should_carry_the_imported_release()
        {
            var payload = Subject.BuildOnReleaseImportPayload(ImportMessage(_main, true));

            payload.IsUpgrade.Should().BeTrue();
            payload.Album.AlbumRelease.MBId.Should().Be("main-mbid");
        }

        [Test]
        public void import_without_release_should_omit_it()
        {
            var payload = Subject.BuildOnReleaseImportPayload(ImportMessage(null, false));

            payload.Album.AlbumRelease.Should().BeNull();
        }
    }
}
