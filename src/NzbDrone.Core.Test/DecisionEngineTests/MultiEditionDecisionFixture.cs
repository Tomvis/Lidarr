using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.AutoMoq;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // Tomvis fork (MUSIC-63): search results are matched to one edition of an album with several monitored
    public static class EditionTestData
    {
        // Ihsahn (2024) as MusicBrainz has it: main digital + orchestral digital monitored, the rest not
        public static Album Ihsahn(bool orchestralMonitored = true)
        {
            var album = new Album { Id = 7, Title = "Ihsahn" };
            album.AlbumReleases = new List<AlbumRelease>
            {
                new AlbumRelease { Id = 1, AlbumId = 7, Title = "Ihsahn", Monitored = true, TrackCount = 11 },
                new AlbumRelease { Id = 2, AlbumId = 7, Title = "Ihsahn (orchestral version)", Monitored = orchestralMonitored, TrackCount = 11 },
                new AlbumRelease { Id = 3, AlbumId = 7, Title = "Ihsahn (orchestral)", Monitored = false, TrackCount = 11 },
                new AlbumRelease { Id = 4, AlbumId = 7, Title = "Ihsahn", Monitored = false, TrackCount = 22 },
                new AlbumRelease { Id = 5, AlbumId = 7, Title = "Ihsahn", Disambiguation = "instrumentals", Monitored = false, TrackCount = 11 }
            };

            return album;
        }

        public static RemoteAlbum Result(Album album, string title)
        {
            var artist = Builder<Artist>.CreateNew()
                .With(c => c.QualityProfile = new QualityProfile
                {
                    UpgradeAllowed = true,
                    Cutoff = Quality.FLAC.Id,
                    Items = Qualities.QualityFixture.GetDefaultQualities(),
                    FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems("None"),
                    MinFormatScore = 0,
                })
                .Build();

            return new RemoteAlbum
            {
                Artist = artist,
                Release = new ReleaseInfo { Title = title },
                ParsedAlbumInfo = new ParsedAlbumInfo { Quality = new QualityModel(Quality.FLAC) },
                Albums = new List<Album> { album },
                CustomFormats = new List<CustomFormat>()
            };
        }

        // main edition complete in FLAC, orchestral edition missing
        public static void GivenMainCompleteOrchestralMissing(AutoMoqer mocker)
        {
            var flac = new TrackFile { Id = 100, Quality = new QualityModel(Quality.FLAC) };

            mocker.GetMock<ITrackService>()
                .Setup(s => s.TracksWithoutFiles(7))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 2 } });
            mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(1))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 1, TrackFileId = 100 } });
            mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(2))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 2, TrackFileId = 0 } });

            mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAlbum(7))
                .Returns(new List<TrackFile> { flac });
            mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByRelease(1))
                .Returns(new List<TrackFile> { flac });
            mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByRelease(2))
                .Returns(new List<TrackFile>());

            mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<TrackFile>()))
                .Returns(new List<CustomFormat>());
        }
    }

    [TestFixture]
    public class EditionTargetFixture
    {
        [TestCase("Ihsahn - Ihsahn (2024) [FLAC 24-96]", 1)]
        [TestCase("Ihsahn - Ihsahn (Orchestral Version) (2024) [FLAC]", 2)]
        [TestCase("Ihsahn-Ihsahn_Orchestral_Version-WEB-2024", 2)]
        [TestCase("Ihsahn - Ihsahn (Orchestral) [2024]", 2)]
        public void should_match_a_result_to_a_monitored_edition(string title, int expectedRelease)
        {
            AlbumEditions.TargetEdition(EditionTestData.Ihsahn(), title).Id.Should().Be(expectedRelease);
        }

        [TestCase("Ihsahn - Ihsahn (Instrumentals) (2024) [FLAC]")]
        [TestCase("Ihsahn - Ihsahn (Instrumental Version) [2024]")]
        public void should_not_match_an_unmonitored_edition(string title)
        {
            AlbumEditions.TargetEdition(EditionTestData.Ihsahn(), title).Should().BeNull();
        }
    }

    [TestFixture]
    public class EditionSpecificationFixture : CoreTest<EditionSpecification>
    {
        [Test]
        public void should_reject_a_result_for_an_unmonitored_edition()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Ihsahn(), "Ihsahn - Ihsahn (Instrumentals) [FLAC]"), null)
                .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_result_for_a_monitored_edition()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Ihsahn(), "Ihsahn - Ihsahn (Orchestral Version) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_anything_when_one_release_is_monitored()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Ihsahn(false), "Ihsahn - Ihsahn (Instrumentals) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class UpgradeDiskMultiEditionFixture : CoreTest<UpgradeDiskSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            EditionTestData.GivenMainCompleteOrchestralMissing(Mocker);
        }

        [Test]
        public void should_reject_a_result_for_the_complete_edition_although_another_is_missing()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Ihsahn(), "Ihsahn - Ihsahn (2024) [FLAC]"), null)
                .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_result_for_the_missing_edition()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Ihsahn(), "Ihsahn - Ihsahn (Orchestral Version) (2024) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class CutoffMultiEditionFixture : CoreTest<CutoffSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            EditionTestData.GivenMainCompleteOrchestralMissing(Mocker);
        }

        [Test]
        public void should_reject_a_result_for_the_complete_edition_although_another_is_missing()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Ihsahn(), "Ihsahn - Ihsahn (2024) [FLAC]"), null)
                .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_result_for_the_missing_edition()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Ihsahn(), "Ihsahn - Ihsahn (Orchestral Version) (2024) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }
    }
}
