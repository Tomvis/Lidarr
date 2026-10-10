using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // Tomvis fork (MUSIC-64): wanted tracks follow their recording when the album switches release
    [TestFixture]
    public class TrackServiceMonitoringFixture : CoreTest<TrackService>
    {
        private static List<Track> Tracks(int releaseId, params (string Recording, bool Wanted)[] tracks)
        {
            return tracks.Select((t, i) => new Track
            {
                Id = (releaseId * 100) + i + 1,
                AlbumReleaseId = releaseId,
                ForeignRecordingId = t.Recording,
                Monitored = t.Wanted
            }).ToList();
        }

        [Test]
        public void should_do_nothing_when_previous_release_wanted_every_track()
        {
            var previous = Tracks(1, ("a", true), ("b", true));
            var current = Tracks(2, ("a", true), ("b", true), ("c", true));

            Subject.CarryOverMonitoring(previous, current);

            current.Should().OnlyContain(t => t.Monitored);
            Mocker.GetMock<ITrackRepository>().Verify(v => v.SetMonitored(It.IsAny<List<Track>>()), Times.Never());
        }

        [Test]
        public void should_want_only_recordings_wanted_on_previous_release()
        {
            var previous = Tracks(1, ("a", false), ("b", true), ("c", false));
            var current = Tracks(2, ("a", true), ("b", true), ("d", true));

            Subject.CarryOverMonitoring(previous, current);

            current.Where(t => t.Monitored).Select(t => t.ForeignRecordingId).Should().BeEquivalentTo(new[] { "b" });
            Mocker.GetMock<ITrackRepository>()
                .Verify(v => v.SetMonitored(It.Is<List<Track>>(l => l.Select(t => t.ForeignRecordingId).OrderBy(x => x).SequenceEqual(new[] { "a", "d" }))), Times.Once());
        }
    }
}
