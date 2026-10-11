using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // Tomvis fork (MUSIC-64): want or unwant single tracks of an album
    [TestFixture]
    public class TrackMonitoringServiceFixture : CoreTest<TrackMonitoringService>
    {
        private Album _album;
        private AlbumRelease _release;
        private AlbumRelease _otherRelease;
        private List<Track> _tracks;
        private List<Track> _otherTracks;

        [SetUp]
        public void Setup()
        {
            _album = new Album { Id = 7, Monitored = false };
            _release = new AlbumRelease { Id = 1, AlbumId = 7, Monitored = true };
            _otherRelease = new AlbumRelease { Id = 2, AlbumId = 7, Monitored = false };
            _tracks = Enumerable.Range(1, 4).Select(i => new Track { Id = i, AlbumReleaseId = 1, Monitored = true }).ToList();
            _otherTracks = new List<Track> { new Track { Id = 10, AlbumReleaseId = 2, Monitored = true } };

            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracks(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => _tracks.Concat(_otherTracks).Where(t => ids.Contains(t.Id)).ToList());
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracksByRelease(1)).Returns(_tracks);
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetRelease(1)).Returns(_release);
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetRelease(2)).Returns(_otherRelease);
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbum(7)).Returns(_album);
            Mocker.GetMock<IAlbumService>().Setup(s => s.SetMonitored(It.IsAny<IEnumerable<int>>(), It.IsAny<bool>()))
                .Callback<IEnumerable<int>, bool>((ids, m) => _album.Monitored = m);
        }

        private IEnumerable<int> Wanted()
        {
            return _tracks.Where(t => t.Monitored).Select(t => t.Id);
        }

        [Test]
        public void wanting_a_track_of_an_unmonitored_album_should_want_only_that_track()
        {
            Subject.SetMonitored(new List<int> { 2 }, true);

            _album.Monitored.Should().BeTrue();
            Wanted().Should().BeEquivalentTo(new[] { 2 });
        }

        [Test]
        public void wanting_a_track_of_an_album_that_wants_every_track_should_change_nothing()
        {
            _album.Monitored = true;

            Subject.SetMonitored(new List<int> { 2 }, true);

            Wanted().Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
            Mocker.GetMock<ITrackService>().Verify(v => v.UpdateMonitored(It.Is<List<Track>>(l => l.Any())), Times.Never());
        }

        [Test]
        public void wanting_another_track_of_a_tracks_only_album_should_add_it()
        {
            _album.Monitored = true;
            _tracks.ForEach(t => t.Monitored = t.Id == 1);

            Subject.SetMonitored(new List<int> { 3 }, true);

            Wanted().Should().BeEquivalentTo(new[] { 1, 3 });
        }

        [Test]
        public void unwanting_a_track_should_keep_the_album_monitored()
        {
            _album.Monitored = true;

            Subject.SetMonitored(new List<int> { 2 }, false);

            _album.Monitored.Should().BeTrue();
            Wanted().Should().BeEquivalentTo(new[] { 1, 3, 4 });
        }

        [Test]
        public void unwanting_the_last_wanted_track_should_unmonitor_the_album_and_reset_its_tracks()
        {
            _album.Monitored = true;
            _tracks.ForEach(t => t.Monitored = t.Id == 1);

            Subject.SetMonitored(new List<int> { 1 }, false);

            _album.Monitored.Should().BeFalse();
            Wanted().Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
        }

        [Test]
        public void a_change_should_publish_album_edited_so_stats_refresh()
        {
            _album.Monitored = true;

            Subject.SetMonitored(new List<int> { 2 }, false);

            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.Is<AlbumEditedEvent>(e => e.Album.Id == 7)), Times.Once());
        }

        [Test]
        public void no_change_should_publish_nothing()
        {
            _album.Monitored = true;

            Subject.SetMonitored(new List<int> { 2 }, true);

            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.IsAny<AlbumEditedEvent>()), Times.Never());
        }

        [Test]
        public void tracks_of_an_unmonitored_release_should_be_refused()
        {
            Assert.Throws<InvalidOperationException>(() => Subject.SetMonitored(new List<int> { 10 }, true));
        }
    }
}
