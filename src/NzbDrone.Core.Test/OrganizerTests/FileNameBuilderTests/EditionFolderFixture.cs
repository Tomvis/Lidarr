using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    // Tomvis fork (MUSIC-63): extra editions of one album get their own folder
    public class EditionFolderFixture : CoreTest<FileNameBuilder>
    {
        private Artist _artist;
        private Album _album;
        private AlbumRelease _main;
        private AlbumRelease _extra;
        private TrackFile _trackFile;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _artist = Builder<Artist>.CreateNew()
                .With(s => s.Name = "Ihsahn")
                .With(s => s.Metadata = new ArtistMetadata { Name = "Ihsahn" })
                .Build();

            _album = Builder<Album>.CreateNew()
                .With(s => s.Title = "Ihsahn")
                .With(s => s.ReleaseDate = new DateTime(2024, 2, 16))
                .With(s => s.AlbumType = "Album")
                .Build();

            _main = Release(1, "Ihsahn", "Digital Media");
            _extra = Release(2, "Ihsahn (orchestral version)", "Digital Media");
            _album.AlbumReleases = new List<AlbumRelease> { _main, _extra };

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameTracks = true;
            _namingConfig.StandardTrackFormat = "{Artist CleanName} - {Album Type} - {Release Year} - {Album CleanTitle}/{medium:00}{track:00} - {Track CleanTitle}";

            Mocker.GetMock<INamingConfigService>()
                .Setup(c => c.GetConfig()).Returns(_namingConfig);

            _trackFile = Builder<TrackFile>.CreateNew()
                .With(e => e.Quality = new QualityModel(Quality.FLAC))
                .Build();

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));
        }

        private AlbumRelease Release(int id, string title, string format)
        {
            return Builder<AlbumRelease>.CreateNew()
                .With(r => r.Id = id)
                .With(r => r.Title = title)
                .With(r => r.Disambiguation = null)
                .With(r => r.Country = new List<string> { "[Worldwide]" })
                .With(r => r.Media = new List<Medium> { new Medium { Number = 1, Format = format } })
                .With(r => r.Monitored = true)
                .Build();
        }

        private string NameFor(AlbumRelease release)
        {
            var track = Builder<Track>.CreateNew()
                .With(e => e.Title = "Cervus Venator")
                .With(e => e.AbsoluteTrackNumber = 1)
                .With(e => e.MediumNumber = 1)
                .With(e => e.AlbumReleaseId = release.Id)
                .With(e => e.AlbumRelease = release)
                .Build();

            return Subject.BuildTrackFileName(new List<Track> { track }, _artist, _album, _trackFile);
        }

        [Test]
        public void should_keep_the_album_folder_when_one_release_is_monitored()
        {
            _main.Monitored = false;

            NameFor(_extra).Should().Be("Ihsahn - Album - 2024 - Ihsahn\\0101 - Cervus Venator".AsOsAgnostic());
        }

        [Test]
        public void should_keep_the_album_folder_for_the_primary_edition()
        {
            NameFor(_main).Should().Be("Ihsahn - Album - 2024 - Ihsahn\\0101 - Cervus Venator".AsOsAgnostic());
        }

        [Test]
        public void should_use_the_edition_title_for_an_extra_edition()
        {
            NameFor(_extra).Should().Be("Ihsahn - Album - 2024 - Ihsahn orchestral version\\0101 - Cervus Venator".AsOsAgnostic());
        }

        [Test]
        public void should_qualify_a_same_titled_extra_edition_by_disambiguation()
        {
            _extra.Title = "Ihsahn";
            _extra.Disambiguation = "deluxe";

            NameFor(_extra).Should().Be("Ihsahn - Album - 2024 - Ihsahn deluxe\\0101 - Cervus Venator".AsOsAgnostic());
        }

        [Test]
        public void should_qualify_a_same_titled_extra_edition_by_format_and_country()
        {
            _extra.Title = "Ihsahn";
            _extra.Media = new List<Medium> { new Medium { Number = 1, Format = "CD" } };
            _extra.Country = new List<string> { "Japan" };

            NameFor(_extra).Should().Be("Ihsahn - Album - 2024 - Ihsahn CD Japan\\0101 - Cervus Venator".AsOsAgnostic());
        }
    }
}
