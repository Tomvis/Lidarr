using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
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

        // Tomvis fork (MUSIC-102): Elvis Presley (1956) as MusicBrainz has it. The owned 2005 UK CD is the primary; a 1999 US CD
        // with the same title is held. Its id is lower, so lowest-id alone would make it the primary.
        public static Album Elvis(bool held = true)
        {
            var album = new Album { Id = 8, Title = "Elvis Presley", ReleaseDate = new DateTime(1956, 3, 23) };
            album.AlbumReleases = new List<AlbumRelease>
            {
                Release(11, "Elvis Presley", null, 1999, "RCA", "United States", "CD", true, held),
                Release(12, "Elvis Presley", null, 2005, "RCA", "United Kingdom", "CD", true, false),
                Release(13, "Elvis Presley", null, 2010, "Music on Vinyl", "Netherlands", "12\" Vinyl", false, false),
                Release(14, "Elvis Presley", "Reissue, Remastered, Stereo", 2005, "BMG", "United States", "CD", false, false),
                Release(15, "Elvis Presley (Legacy Edition)", null, 2011, "Legacy", null, "CD", false, false)
            };

            return album;
        }

        // Tomvis fork (MUSIC-102): the original 1995 CD held as the DR target of an owned 2012 remaster
        public static Album Remaster(string heldFormat = "CD")
        {
            var album = new Album { Id = 9, Title = "Album", ReleaseDate = new DateTime(1995, 1, 1) };
            album.AlbumReleases = new List<AlbumRelease>
            {
                Release(21, "Album", null, 2012, "Sony Music", "Europe", "CD", true, false),
                Release(22, "Album", null, 1995, "Epic", "Japan", heldFormat, true, true)
            };

            return album;
        }

        public static AlbumRelease Release(int id, string title, string disambiguation, int year, string label, string country, string format, bool monitored, bool held)
        {
            return new AlbumRelease
            {
                Id = id,
                Title = title,
                Disambiguation = disambiguation,
                ReleaseDate = new DateTime(year, 1, 1),
                Label = new List<string> { label },
                Country = country == null ? new List<string>() : new List<string> { country },
                Media = new List<Medium> { new Medium { Number = 1, Format = format } },
                Monitored = monitored,
                Held = held,
                TrackCount = 12
            };
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
    public class HeldEditionTargetFixture
    {
        [TestCase("Elvis Presley - Elvis Presley (1999) [FLAC]", 11)]
        [TestCase("Elvis Presley - Elvis Presley [US CD] FLAC", 12)]
        [TestCase("Elvis Presley - Elvis Presley (1956) [USA] FLAC", 11)]
        [TestCase("Elvis Presley-Elvis_Presley-(RCA)-CD-FLAC-1999", 11)]
        [TestCase("Elvis Presley - Elvis Presley (2005) [FLAC]", 12)]
        [TestCase("Elvis Presley - Elvis Presley (1956) [FLAC]", 12)]
        [TestCase("Elvis Presley - Elvis Presley [FLAC]", 12)]
        [TestCase("Elvis Presley - Elvis Presley (1999) (2005 UK) [FLAC]", 12)]
        [TestCase("Elvis Presley - Elvis Presley (1999 USA / 2005) [FLAC]", 11)]
        [TestCase("Elvis Presley - Elvis Presley (1999 / 2005) [FLAC]", 12)]
        public void should_match_a_held_edition_by_its_release_data(string title, int expectedRelease)
        {
            AlbumEditions.TargetEdition(EditionTestData.Elvis(), title).Id.Should().Be(expectedRelease);
        }

        [TestCase("Elvis Presley - Elvis Presley (1999) [FLAC]")]
        [TestCase("Elvis Presley - Elvis Presley (2005) [FLAC]")]
        public void should_match_by_words_only_without_a_held_edition(string title)
        {
            var album = EditionTestData.Elvis(false);
            var primary = AlbumEditions.Primary(album, AlbumEditions.Monitored(album.AlbumReleases.Value));

            AlbumEditions.TargetEdition(album, title).Should().Be(primary);
        }

        [TestCase("Artist - Album (1995) [FLAC]", 22)]
        [TestCase("Artist - Album [Japan] FLAC", 22)]
        [TestCase("Artist - Album (Japanese Edition) FLAC", 22)]
        [TestCase("Artist - Album (2012 Remaster) [FLAC]", 21)]
        [TestCase("Artist - Album (Remastered) [FLAC]", 21)]
        [TestCase("Artist - Album (1995) (2012 Remaster) [FLAC]", 21)]
        [TestCase("Artist - Album [FLAC]", 21)]
        public void should_tell_an_original_from_a_remaster(string title, int expectedRelease)
        {
            AlbumEditions.TargetEdition(EditionTestData.Remaster(), title).Id.Should().Be(expectedRelease);
        }

        [TestCase("Artist - Album [WEB FLAC]", 22)]
        [TestCase("Artist - Album [CD FLAC]", 21)]
        [TestCase("Artist - Album (2012) [WEB FLAC]", 21)]
        public void should_match_a_held_edition_by_format(string title, int expectedRelease)
        {
            AlbumEditions.TargetEdition(EditionTestData.Remaster("Digital Media"), title).Id.Should().Be(expectedRelease);
        }

        [Test]
        public void a_held_edition_should_never_be_the_primary()
        {
            var album = EditionTestData.Elvis();

            AlbumEditions.Primary(album, AlbumEditions.Monitored(album.AlbumReleases.Value)).Id.Should().Be(12);
            AlbumEditions.IsHeld(album, album.AlbumReleases.Value[0]).Should().BeTrue();
            AlbumEditions.IsHeld(album, album.AlbumReleases.Value[1]).Should().BeFalse();
        }

        [Test]
        public void a_held_edition_alone_should_not_be_held()
        {
            var album = EditionTestData.Elvis();
            album.AlbumReleases.Value[1].Monitored = false;

            AlbumEditions.IsHeld(album, album.AlbumReleases.Value[0]).Should().BeFalse();
        }

        [Test]
        public void a_held_edition_title_should_name_its_year()
        {
            var album = EditionTestData.Elvis();

            AlbumEditions.EditionTitle(album, album.AlbumReleases.Value[0]).Should().Be("Elvis Presley (1999 CD United States)");
        }

        [Test]
        public void a_refreshed_release_should_keep_its_hold()
        {
            var local = new AlbumRelease { Id = 1, Monitored = true, Held = true };
            var remote = new AlbumRelease();

            remote.UseDbFieldsFrom(local);

            remote.Held.Should().BeTrue();
        }

        [Test]
        public void a_refreshed_release_should_keep_its_lock()
        {
            var local = new AlbumRelease { Id = 1, Monitored = true, Locked = true };
            var remote = new AlbumRelease();

            remote.UseDbFieldsFrom(local);

            remote.Locked.Should().BeTrue();
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

    [TestFixture]
    public class SingleAlbumSearchMatchMultiEditionFixture : CoreTest<NzbDrone.Core.DecisionEngine.Specifications.Search.SingleAlbumSearchMatchSpecification>
    {
        private Decision Check(Album album, string parsedAlbumTitle)
        {
            var result = EditionTestData.Result(album, $"Ihsahn - {parsedAlbumTitle} (2024) [FLAC]");
            result.ParsedAlbumInfo.AlbumTitle = parsedAlbumTitle;

            var criteria = new AlbumSearchCriteria { AlbumTitle = album.Title, Albums = new List<Album> { album } };

            return Subject.IsSatisfiedBy(result, criteria);
        }

        [Test]
        public void should_accept_a_result_titled_as_a_monitored_edition()
        {
            Check(EditionTestData.Ihsahn(), "Ihsahn (Orchestral Version)").Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_result_titled_as_an_unmonitored_edition()
        {
            Check(EditionTestData.Ihsahn(), "Ihsahn (Orchestral)").Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_an_edition_title_when_one_release_is_monitored()
        {
            Check(EditionTestData.Ihsahn(false), "Ihsahn (Orchestral Version)").Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class QueueMultiEditionFixture : CoreTest<QueueSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<RemoteAlbum>(), It.IsAny<long>()))
                .Returns(new List<CustomFormat>());
        }

        private void GivenQueued(RemoteAlbum queued)
        {
            Mocker.GetMock<IQueueService>()
                .Setup(s => s.GetQueue())
                .Returns(new List<Queue.Queue> { new Queue.Queue { RemoteAlbum = queued, TrackedDownloadState = TrackedDownloadState.Downloading } });
        }

        [Test]
        public void should_not_block_an_edition_on_another_edition_in_the_queue()
        {
            var album = EditionTestData.Ihsahn();
            var orchestral = EditionTestData.Result(album, "Ihsahn - Ihsahn (Orchestral Version) [FLAC]");
            var main = EditionTestData.Result(album, "Ihsahn - Ihsahn [FLAC]");
            main.Artist = orchestral.Artist;
            GivenQueued(orchestral);

            Subject.IsSatisfiedBy(main, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_block_the_same_edition_already_in_the_queue()
        {
            var album = EditionTestData.Ihsahn();
            var queued = EditionTestData.Result(album, "Ihsahn - Ihsahn (Orchestral Version) [FLAC]");
            var again = EditionTestData.Result(album, "Ihsahn-Ihsahn_Orchestral_Version-WEB-FLAC");
            again.Artist = queued.Artist;
            GivenQueued(queued);

            Subject.IsSatisfiedBy(again, null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class ProcessDecisionsMultiEditionFixture : CoreTest<ProcessDownloadDecisions>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IPrioritizeDownloadDecision>()
                .Setup(v => v.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns<List<DownloadDecision>>(v => v);
        }

        private DownloadDecision Decision(Album album, string title)
        {
            var result = EditionTestData.Result(album, title);
            result.Release.DownloadProtocol = "TorrentDownloadProtocol";
            result.Release.PublishDate = System.DateTime.UtcNow;

            return new DownloadDecision(result);
        }

        [Test]
        public async Task should_grab_one_release_per_edition()
        {
            var album = EditionTestData.Ihsahn();

            await Subject.ProcessDecisions(new List<DownloadDecision>
            {
                Decision(album, "Ihsahn - Ihsahn [FLAC]"),
                Decision(album, "Ihsahn - Ihsahn (Orchestral Version) [FLAC]"),
                Decision(album, "Ihsahn-Ihsahn_Orchestral_Version-WEB-FLAC")
            });

            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteAlbum>(), null), Times.Exactly(2));
        }

        [Test]
        public async Task should_grab_one_release_per_album_when_one_release_is_monitored()
        {
            var album = EditionTestData.Ihsahn(false);

            await Subject.ProcessDecisions(new List<DownloadDecision>
            {
                Decision(album, "Ihsahn - Ihsahn [FLAC]"),
                Decision(album, "Ihsahn - Ihsahn (Orchestral Version) [FLAC]")
            });

            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteAlbum>(), null), Times.Once());
        }
    }

    // Tomvis fork (MUSIC-102): a held edition has no files of its own, so a result matched to it is wanted although the
    // owned edition meets cutoff, and a result nothing tells apart is judged by the owned edition's files
    [TestFixture]
    public class CutoffHeldEditionFixture : CoreTest<CutoffSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();

            var flac = new TrackFile { Id = 200, Quality = new QualityModel(Quality.FLAC) };

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.TracksWithoutFiles(8))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 11 } });
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(12))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 12, TrackFileId = 200 } });
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(11))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 11, TrackFileId = 0 } });

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAlbum(8))
                .Returns(new List<TrackFile> { flac });
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByRelease(12))
                .Returns(new List<TrackFile> { flac });
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByRelease(11))
                .Returns(new List<TrackFile>());

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<TrackFile>()))
                .Returns(new List<CustomFormat>());
        }

        [Test]
        public void should_accept_a_result_for_the_held_edition()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Elvis(), "Elvis Presley - Elvis Presley (1999) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_result_nothing_tells_apart_from_the_owned_edition()
        {
            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Elvis(), "Elvis Presley - Elvis Presley [FLAC]"), null)
                .Accepted.Should().BeFalse();
        }
    }

    // Tomvis fork (MUSIC-103): searches for a held edition also find the owned edition; those must not upgrade it
    [TestFixture]
    public class HeldAlbumSpecificationFixture : CoreTest<HeldAlbumSpecification>
    {
        private void GivenOwnedTracks(bool complete)
        {
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(12))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 12, TrackFileId = 200 }, new Track { AlbumReleaseId = 12, TrackFileId = complete ? 201 : 0 } });
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(11))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 11, TrackFileId = 0 } });
        }

        [Test]
        public void should_reject_a_result_for_the_complete_owned_edition_while_one_is_held()
        {
            GivenOwnedTracks(true);

            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Elvis(), "Elvis Presley - Elvis Presley (2005) [FLAC]"), null)
                .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_result_for_the_held_edition()
        {
            GivenOwnedTracks(true);

            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Elvis(), "Elvis Presley - Elvis Presley (1999) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_result_for_the_owned_edition_when_it_misses_tracks()
        {
            GivenOwnedTracks(false);

            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Elvis(), "Elvis Presley - Elvis Presley (2005) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_an_upgrade_when_nothing_is_held()
        {
            GivenOwnedTracks(true);

            Subject.IsSatisfiedBy(EditionTestData.Result(EditionTestData.Elvis(false), "Elvis Presley - Elvis Presley (2005) [FLAC]"), null)
                .Accepted.Should().BeTrue();
        }
    }

    // Tomvis fork (MUSIC-135): an edition bestedition swapped to is locked: custom-format upgrades must not replace it
    [TestFixture]
    public class LockedEditionSpecificationFixture : CoreTest<LockedEditionSpecification>
    {
        // the owned 2012 remaster, locked, with a 2016 "Remastered" reissue unmonitored
        private static Album Owned(bool locked = true)
        {
            var album = new Album { Id = 9, Title = "Album", ReleaseDate = new DateTime(1995, 1, 1) };
            album.AlbumReleases = new List<AlbumRelease>
            {
                EditionTestData.Release(21, "Album", null, 2012, "Sony Music", "Europe", "CD", true, false),
                EditionTestData.Release(23, "Album", "Remastered", 2016, "Sony Music", "Europe", "CD", false, false)
            };
            album.AlbumReleases.Value[0].Locked = locked;

            return album;
        }

        private void GivenTracks(bool ownedComplete)
        {
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(21))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 21, TrackFileId = 300 }, new Track { AlbumReleaseId = 21, TrackFileId = ownedComplete ? 301 : 0 } });
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(22))
                .Returns(new List<Track> { new Track { AlbumReleaseId = 22, TrackFileId = 0 } });
        }

        [TestCase("Album - Album (2012) [FLAC]")]
        [TestCase("Album - Album (2016 Remastered) [FLAC 24bit]")]
        public void should_reject_an_upgrade_of_the_complete_locked_edition(string title)
        {
            GivenTracks(true);

            Subject.IsSatisfiedBy(EditionTestData.Result(Owned(), title), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_result_when_the_locked_edition_misses_tracks()
        {
            GivenTracks(false);

            Subject.IsSatisfiedBy(EditionTestData.Result(Owned(), "Album - Album (2012) [FLAC]"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_an_upgrade_when_nothing_is_locked()
        {
            GivenTracks(true);

            Subject.IsSatisfiedBy(EditionTestData.Result(Owned(false), "Album - Album (2012) [FLAC]"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_result_for_a_held_edition_of_a_locked_album()
        {
            GivenTracks(true);
            var album = EditionTestData.Remaster();
            album.AlbumReleases.Value[0].Locked = true;

            Subject.IsSatisfiedBy(EditionTestData.Result(album, "Album - Album (1995 Japan) [FLAC]"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_result_for_the_locked_edition_while_another_is_held()
        {
            GivenTracks(true);
            var album = EditionTestData.Remaster();
            album.AlbumReleases.Value[0].Locked = true;

            Subject.IsSatisfiedBy(EditionTestData.Result(album, "Album - Album (2012 Europe) [FLAC]"), null).Accepted.Should().BeFalse();
        }
    }
}
