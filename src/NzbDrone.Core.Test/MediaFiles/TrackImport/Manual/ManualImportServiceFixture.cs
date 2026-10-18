using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Manual
{
    // Tomvis fork (MUSIC-126)
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private Artist _artist;
        private Track _track;
        private DownloadClientItem _downloadItem;
        private TrackedDownload _trackedDownload;
        private ManualImportCommand _command;

        [SetUp]
        public void Setup()
        {
            _artist = Builder<Artist>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Path = @"C:\Music\Artist".AsOsAgnostic())
                .Build();

            _track = Builder<Track>.CreateNew().With(t => t.Id = 10).Build();

            _downloadItem = Builder<DownloadClientItem>.CreateNew()
                .With(d => d.DownloadId = "dl1")
                .With(d => d.OutputPath = new OsPath(@"C:\Downloads\Album".AsOsAgnostic()))
                .With(d => d.CanMoveFiles = true)
                .Build();

            // after a restart, an ImportFailed download is never Check()ed, so ImportItem stays null
            _trackedDownload = new TrackedDownload
            {
                DownloadItem = _downloadItem,
                ImportItem = null,
                State = TrackedDownloadState.ImportFailed
            };

            _command = new ManualImportCommand
            {
                ImportMode = ImportMode.Move,
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = @"C:\Downloads\Album-work\01.flac".AsOsAgnostic(),
                        ArtistId = 1,
                        AlbumId = 2,
                        AlbumReleaseId = 3,
                        TrackIds = new List<int> { 10 },
                        Quality = new QualityModel(Quality.FLAC),
                        DownloadId = "dl1"
                    }
                }
            };

            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtist(1)).Returns(_artist);
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbum(2)).Returns(new Album { Id = 2 });
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetRelease(3)).Returns(new AlbumRelease { Id = 3 });
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracks(It.IsAny<IEnumerable<int>>())).Returns(new List<Track> { _track });

            var fileInfo = new Mock<IFileInfo>();
            fileInfo.SetupGet(f => f.Length).Returns(100);
            fileInfo.SetupGet(f => f.LastWriteTimeUtc).Returns(DateTime.UtcNow);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileInfo(It.IsAny<string>())).Returns(fileInfo.Object);

            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolder(It.IsAny<string>())).Returns(new RootFolder());

            Mocker.GetMock<IImportApprovedTracks>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns<List<ImportDecision<LocalTrack>>, bool, DownloadClientItem, ImportMode>((decisions, replace, item, mode) =>
                    decisions.ConvertAll(d => new ImportResult(d)));

            Mocker.GetMock<IProvideImportItemService>()
                .Setup(s => s.ProvideImportItem(_downloadItem, null))
                .Returns(_downloadItem);
        }

        [Test]
        public void should_mark_download_imported_when_tracked_download_has_no_import_item()
        {
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("dl1")).Returns(_trackedDownload);

            Subject.Execute(_command);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
            Mocker.GetMock<IImportApprovedTracks>()
                .Verify(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), _downloadItem, ImportMode.Move), Times.Once());
        }

        [Test]
        public void should_import_without_download_item_when_download_is_no_longer_tracked()
        {
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("dl1")).Returns((TrackedDownload)null);

            Subject.Execute(_command);

            Mocker.GetMock<IImportApprovedTracks>()
                .Verify(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), null, ImportMode.Move), Times.Once());
        }
    }
}
