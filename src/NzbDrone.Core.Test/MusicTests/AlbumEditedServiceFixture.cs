using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // Tomvis fork (MUSIC-63): changing the monitored edition set only unlinks the editions that left it
    [TestFixture]
    public class AlbumEditedServiceFixture : CoreTest<AlbumEditedService>
    {
        private Album AlbumMonitoring(params int[] monitored)
        {
            var releases = Enumerable.Range(1, 3)
                .Select(id => new AlbumRelease { Id = id, Monitored = monitored.Contains(id) })
                .ToList();

            return Builder<Album>.CreateNew()
                .With(a => a.Id = 7)
                .With(a => a.AnyReleaseOk = false)
                .With(a => a.Artist = new Artist { Path = "/music/artist" })
                .With(a => a.AlbumReleases = releases)
                .Build();
        }

        private void GivenTracks()
        {
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByAlbum(7))
                .Returns(new List<Track> { new Track { Id = 10, TrackFileId = 100 }, new Track { Id = 20, TrackFileId = 200 } });

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByReleases(It.Is<List<int>>(l => l.SequenceEqual(new[] { 2 }))))
                .Returns(new List<Track> { new Track { Id = 20, AlbumReleaseId = 2, TrackFileId = 200 } });
        }

        private void Edit(Album oldAlbum, Album newAlbum)
        {
            GivenTracks();
            Subject.Handle(new AlbumEditedEvent(newAlbum, oldAlbum));
        }

        [Test]
        public void should_unlink_every_track_when_the_single_monitored_release_changes()
        {
            Edit(AlbumMonitoring(1), AlbumMonitoring(2));

            Mocker.GetMock<ITrackService>()
                .Verify(v => v.SetFileIds(It.Is<List<Track>>(l => l.Count == 2 && l.All(t => t.TrackFileId == 0))), Times.Once());
        }

        [Test]
        public void should_not_unlink_anything_when_an_edition_is_added()
        {
            Edit(AlbumMonitoring(1), AlbumMonitoring(1, 2));

            Mocker.GetMock<ITrackService>()
                .Verify(v => v.SetFileIds(It.IsAny<List<Track>>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void should_only_unlink_the_edition_that_was_removed()
        {
            Edit(AlbumMonitoring(1, 2), AlbumMonitoring(1));

            Mocker.GetMock<ITrackService>()
                .Verify(v => v.SetFileIds(It.Is<List<Track>>(l => l.Count == 1 && l[0].Id == 20 && l[0].TrackFileId == 0)), Times.Once());
        }

        [Test]
        public void should_do_nothing_when_the_edition_set_is_unchanged()
        {
            Edit(AlbumMonitoring(1, 2), AlbumMonitoring(1, 2));

            Mocker.GetMock<ITrackService>()
                .Verify(v => v.SetFileIds(It.IsAny<List<Track>>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }
    }
}
