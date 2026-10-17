using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Aggregation;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport
{
    [TestFixture]
    public class ImportDecisionMakerFixture : FileSystemTest<ImportDecisionMaker>
    {
        private List<IFileInfo> _fileInfos;
        private LocalTrack _localTrack;
        private Artist _artist;
        private Album _album;
        private AlbumRelease _albumRelease;
        private QualityModel _quality;

        private IdentificationOverrides _idOverrides;
        private ImportDecisionMakerConfig _idConfig;

        private Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>> _albumpass1;
        private Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>> _albumpass2;
        private Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>> _albumpass3;

        private Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>> _albumfail1;
        private Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>> _albumfail2;
        private Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>> _albumfail3;

        private Mock<IImportDecisionEngineSpecification<LocalTrack>> _pass1;
        private Mock<IImportDecisionEngineSpecification<LocalTrack>> _pass2;
        private Mock<IImportDecisionEngineSpecification<LocalTrack>> _pass3;

        private Mock<IImportDecisionEngineSpecification<LocalTrack>> _fail1;
        private Mock<IImportDecisionEngineSpecification<LocalTrack>> _fail2;
        private Mock<IImportDecisionEngineSpecification<LocalTrack>> _fail3;

        [SetUp]
        public void Setup()
        {
            _albumpass1 = new Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>>();
            _albumpass2 = new Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>>();
            _albumpass3 = new Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>>();

            _albumfail1 = new Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>>();
            _albumfail2 = new Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>>();
            _albumfail3 = new Mock<IImportDecisionEngineSpecification<LocalAlbumRelease>>();

            _pass1 = new Mock<IImportDecisionEngineSpecification<LocalTrack>>();
            _pass2 = new Mock<IImportDecisionEngineSpecification<LocalTrack>>();
            _pass3 = new Mock<IImportDecisionEngineSpecification<LocalTrack>>();

            _fail1 = new Mock<IImportDecisionEngineSpecification<LocalTrack>>();
            _fail2 = new Mock<IImportDecisionEngineSpecification<LocalTrack>>();
            _fail3 = new Mock<IImportDecisionEngineSpecification<LocalTrack>>();

            _albumpass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _albumpass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _albumpass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());

            _albumfail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_albumfail1"));
            _albumfail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_albumfail2"));
            _albumfail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_albumfail3"));

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_fail3"));

            _artist = Builder<Artist>.CreateNew()
                .With(e => e.QualityProfileId = 1)
                .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .Build();

            _album = Builder<Album>.CreateNew()
                .With(x => x.Artist = _artist)
                .Build();

            _albumRelease = Builder<AlbumRelease>.CreateNew()
                .With(x => x.Album = _album)
                .Build();

            _quality = new QualityModel(Quality.MP3_256);

            _localTrack = new LocalTrack
            {
                Artist = _artist,
                Quality = _quality,
                Tracks = new List<Track> { new Track() },
                Path = @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV.avi".AsOsAgnostic()
            };

            _idOverrides = new IdentificationOverrides
            {
                Artist = _artist
            };

            _idConfig = new ImportDecisionMakerConfig();

            GivenAudioFiles(new List<string> { @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV.avi".AsOsAgnostic() });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalTrack>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalTrack> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                {
                    var ret = new LocalAlbumRelease(tracks);
                    ret.AlbumRelease = _albumRelease;
                    return new List<LocalAlbumRelease> { ret };
                });

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.FilterUnchangedFiles(It.IsAny<List<IFileInfo>>(), It.IsAny<FilterFilesType>()))
                .Returns((List<IFileInfo> files, FilterFilesType filter) => files);

            Mocker.GetMock<IHistoryService>()
                .Setup(x => x.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory>());

            GivenSpecifications(_albumpass1);
        }

        private void GivenSpecifications<T>(params Mock<IImportDecisionEngineSpecification<T>>[] mocks)
        {
            Mocker.SetConstant(mocks.Select(c => c.Object));
        }

        private void GivenAudioFiles(IEnumerable<string> videoFiles)
        {
            foreach (var file in videoFiles)
            {
                FileSystem.AddFile(file, new MockFileData(string.Empty));
            }

            _fileInfos = videoFiles.Select(x => DiskProvider.GetFileInfo(x)).ToList();
        }

        private void GivenAugmentationSuccess()
        {
            Mocker.GetMock<IAugmentingService>()
                  .Setup(s => s.Augment(It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Callback<LocalTrack, bool>((localTrack, otherFiles) =>
                  {
                      localTrack.Tracks = _localTrack.Tracks;
                  });
        }

        [Test]
        public void should_call_all_album_specifications()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            var itemInfo = new ImportDecisionMakerInfo { DownloadClientItem = downloadClientItem };

            GivenAugmentationSuccess();
            GivenSpecifications(_albumpass1, _albumpass2, _albumpass3, _albumfail1, _albumfail2, _albumfail3);

            Subject.GetImportDecisions(_fileInfos, null, itemInfo, _idConfig);

            _albumfail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _albumfail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _albumfail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _albumpass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _albumpass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _albumpass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalAlbumRelease>(), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        [Test]
        public void should_call_all_track_specifications_if_album_accepted()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            var itemInfo = new ImportDecisionMakerInfo { DownloadClientItem = downloadClientItem };

            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_fileInfos, null, itemInfo, _idConfig);

            _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        [Test]
        public void should_call_no_track_specifications_if_album_rejected()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            var itemInfo = new ImportDecisionMakerInfo { DownloadClientItem = downloadClientItem };

            GivenAugmentationSuccess();
            GivenSpecifications(_albumpass1, _albumpass2, _albumpass3, _albumfail1, _albumfail2, _albumfail3);
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_fileInfos, null, itemInfo, _idConfig);

            _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalTrack>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void should_return_rejected_if_only_album_spec_fails()
        {
            GivenSpecifications(_albumfail1);
            GivenSpecifications(_pass1);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_only_track_spec_fails()
        {
            GivenSpecifications(_albumpass1);
            GivenSpecifications(_fail1);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_album_spec_fails()
        {
            GivenSpecifications(_albumpass1, _albumfail1, _albumpass2, _albumpass3);
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_track_spec_fails()
        {
            GivenSpecifications(_albumpass1, _albumpass2, _albumpass3);
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_approved_if_all_specs_pass()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_albumpass1, _albumpass2, _albumpass3);
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_reject_more_than_one_version_of_an_album()
        {
            GivenAugmentationSuccess();

            GivenAudioFiles(new[]
            {
                @"C:\Test\Unsorted\release1\track1.mp3".AsOsAgnostic(),
                @"C:\Test\Unsorted\release2\track1.mp3".AsOsAgnostic()
            });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalTrack>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalTrack> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                {
                    var ret1 = new LocalAlbumRelease(tracks.Take(1).ToList());
                    ret1.AlbumRelease = _albumRelease;

                    var ret2 = new LocalAlbumRelease(tracks.Skip(1).ToList());
                    var albumRelease2 = Builder<AlbumRelease>.CreateNew()
                        .With(x => x.Album = _album)
                        .With(x => x.ForeignReleaseId = "anotherid")
                        .Build();
                    ret2.AlbumRelease = albumRelease2;

                    return new List<LocalAlbumRelease> { ret1, ret2 };
                });

            GivenSpecifications(_pass1);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);
            result.Count(x => x.Approved).Should().Be(1);
            result.Count(x => !x.Approved).Should().Be(1);
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_blowup_the_process_due_to_failed_augment()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IAugmentingService>()
                  .Setup(c => c.Augment(It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Throws<TestException>();

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            var decisions = Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

            Mocker.GetMock<IAugmentingService>()
                  .Verify(c => c.Augment(It.IsAny<LocalTrack>(), It.IsAny<bool>()), Times.Exactly(_fileInfos.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_not_throw_if_release_not_identified()
        {
            GivenSpecifications(_pass1);

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalTrack>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalTrack> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                    {
                        return new List<LocalAlbumRelease> { new LocalAlbumRelease(tracks) };
                    });

            var decisions = Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

            Mocker.GetMock<IAugmentingService>()
                  .Verify(c => c.Augment(It.IsAny<LocalTrack>(), It.IsAny<bool>()), Times.Exactly(_fileInfos.Count));

            decisions.Should().HaveCount(3);
            decisions.First().Rejections.Should().NotBeEmpty();
        }

        [Test]
        public void should_not_throw_if_tracks_are_not_found()
        {
            GivenSpecifications(_pass1);

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            var decisions = Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

            Mocker.GetMock<IAugmentingService>()
                  .Verify(c => c.Augment(It.IsAny<LocalTrack>(), It.IsAny<bool>()), Times.Exactly(_fileInfos.Count));

            decisions.Should().HaveCount(3);
            decisions.First().Rejections.Should().NotBeEmpty();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            Mocker.GetMock<IAugmentingService>()
                  .Setup(c => c.Augment(It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Throws<TestException>();

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }

        private void GivenRescanOfLinkedFile(string tagReleaseId, string tagTrackId, bool releaseMonitored = true)
        {
            _idConfig = new ImportDecisionMakerConfig { Filter = FilterFilesType.Known };

            Mocker.GetMock<IAudioTagService>()
                  .Setup(s => s.ReadTags(It.IsAny<string>()))
                  .Returns(new ParsedTrackInfo { ReleaseMBId = tagReleaseId, TrackMBId = tagTrackId });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFileWithPath(It.IsAny<List<string>>()))
                  .Returns((List<string> paths) => paths.Select(p => new TrackFile
                  {
                      Path = p,
                      AlbumId = 1,
                      Tracks = new List<Track> { new Track { ForeignTrackId = "track-1", AlbumReleaseId = 5 } }
                  }).ToList());

            Mocker.GetMock<IReleaseService>()
                  .Setup(s => s.GetReleaseByForeignReleaseId("release-1", true))
                  .Returns(new AlbumRelease { Id = 5, ForeignReleaseId = "release-1", Monitored = releaseMonitored });

            Mocker.GetMock<IAugmentingService>()
                  .Setup(s => s.Augment(It.IsAny<LocalTrack>(), It.IsAny<bool>()));
        }

        [Test]
        public void rescan_should_not_reidentify_a_changed_file_whose_tags_still_name_its_linked_track()
        {
            GivenRescanOfLinkedFile("release-1", "track-1");

            var decisions = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            Mocker.GetMock<IIdentificationService>()
                  .Verify(s => s.Identify(It.Is<List<LocalTrack>>(l => l.Any()), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Never());
            decisions.Should().ContainSingle().Which.Approved.Should().BeFalse();
        }

        [TestCase("release-2", "track-1")]
        [TestCase("release-1", "track-2")]
        [TestCase(null, "track-1")]
        [TestCase("release-1", null)]
        public void rescan_should_reidentify_a_changed_file_whose_tags_name_other_ids(string tagReleaseId, string tagTrackId)
        {
            GivenRescanOfLinkedFile(tagReleaseId, tagTrackId);

            Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            Mocker.GetMock<IIdentificationService>()
                  .Verify(s => s.Identify(It.Is<List<LocalTrack>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void rescan_should_reidentify_a_file_linked_to_an_unmonitored_release()
        {
            GivenRescanOfLinkedFile("release-1", "track-1", releaseMonitored: false);

            Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            Mocker.GetMock<IIdentificationService>()
                  .Verify(s => s.Identify(It.Is<List<LocalTrack>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void rescan_should_reidentify_when_the_release_lookup_throws()
        {
            GivenRescanOfLinkedFile("release-1", "track-1");
            Mocker.GetMock<IReleaseService>()
                  .Setup(s => s.GetReleaseByForeignReleaseId("release-1", true))
                  .Throws(new System.InvalidOperationException("Sequence contains more than one element"));

            Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            Mocker.GetMock<IIdentificationService>()
                  .Verify(s => s.Identify(It.Is<List<LocalTrack>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void manual_import_should_identify_even_when_tags_name_the_linked_track()
        {
            GivenRescanOfLinkedFile("release-1", "track-1");
            _idConfig.Filter = FilterFilesType.None;

            Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            Mocker.GetMock<IIdentificationService>()
                  .Verify(s => s.Identify(It.Is<List<LocalTrack>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }
    }
}
