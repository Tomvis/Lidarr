using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class UpdateTrackFileServiceFixture : CoreTest<UpdateTrackFileService>
    {
        private Artist _artist;
        private Album _album;
        private TrackFile _trackFile;
        private DateTime _releaseDate;
        private DateTime _newWriteTime;

        [SetUp]
        public void Setup()
        {
            _releaseDate = new DateTime(2018, 9, 7, 0, 0, 0, DateTimeKind.Utc);
            _newWriteTime = new DateTime(2018, 9, 6, 21, 0, 0, DateTimeKind.Utc);

            _artist = Builder<Artist>.CreateNew()
                .With(a => a.Path = @"C:\Test\Music\Artist".AsOsAgnostic())
                .Build();

            _album = Builder<Album>.CreateNew()
                .With(a => a.ReleaseDate = _releaseDate)
                .Build();

            _trackFile = Builder<TrackFile>.CreateNew()
                .With(f => f.AlbumId = _album.Id)
                .With(f => f.Path = @"C:\Test\Music\Artist\Album\01 - Track.flac".AsOsAgnostic())
                .With(f => f.Modified = new DateTime(2026, 10, 9, 12, 16, 5, DateTimeKind.Utc))
                .Build();

            var track = Builder<Track>.CreateNew()
                .With(t => t.TrackFileId = _trackFile.Id)
                .With(t => t.TrackFile = _trackFile)
                .Build();

            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.FileDate)
                .Returns(FileDateType.AlbumReleaseDate);

            Mocker.GetMock<IAlbumService>()
                .Setup(s => s.GetAlbum(_album.Id))
                .Returns(_album);

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.TracksWithFiles(_artist.Id))
                .Returns(new List<Track> { track });

            // first read = before the change, second read = after it
            Mocker.GetMock<IDiskProvider>()
                .SetupSequence(d => d.FileGetLastWrite(_trackFile.Path))
                .Returns(_trackFile.Modified)
                .Returns(_newWriteTime);
        }

        [Test]
        public void should_store_new_write_time_when_changing_file_date_on_scan()
        {
            Subject.Handle(new ArtistScannedEvent(_artist));

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FileSetLastWriteTime(_trackFile.Path, _releaseDate), Times.Once());

            // a stale Modified makes the next scan re-import every retagged file
            Mocker.GetMock<IMediaFileService>()
                .Verify(s => s.Update(It.Is<List<TrackFile>>(l => l.Count == 1 && l[0].Modified == _newWriteTime)), Times.Once());
        }

        [Test]
        public void should_set_new_write_time_on_trackfile_when_changing_file_date_for_file()
        {
            Subject.ChangeFileDateForFile(_trackFile, _artist, new List<Track>());

            _trackFile.Modified.Should().Be(_newWriteTime);
        }

        [Test]
        public void should_not_update_db_when_file_date_unchanged()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileGetLastWrite(_trackFile.Path))
                .Returns(_releaseDate);

            Subject.Handle(new ArtistScannedEvent(_artist));

            Mocker.GetMock<IMediaFileService>()
                .Verify(s => s.Update(It.IsAny<List<TrackFile>>()), Times.Never());
        }
    }
}
