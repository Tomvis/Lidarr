using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Music
{
    public interface ITrackMonitoringService
    {
        void SetMonitored(List<int> trackIds, bool monitored);
    }

    // Tomvis fork (MUSIC-64): want or unwant single tracks. Wanting a track of an unmonitored album monitors the album
    // for that track only; unwanting the last wanted track unmonitors the album and resets its tracks to wanted.
    public class TrackMonitoringService : ITrackMonitoringService
    {
        private readonly ITrackService _trackService;
        private readonly IReleaseService _releaseService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;

        public TrackMonitoringService(ITrackService trackService,
                                      IReleaseService releaseService,
                                      IAlbumService albumService,
                                      Logger logger)
        {
            _trackService = trackService;
            _releaseService = releaseService;
            _albumService = albumService;
            _logger = logger;
        }

        public void SetMonitored(List<int> trackIds, bool monitored)
        {
            var requested = _trackService.GetTracks(trackIds);

            foreach (var releaseId in requested.Select(t => t.AlbumReleaseId).Distinct())
            {
                var release = _releaseService.GetRelease(releaseId);

                if (!release.Monitored)
                {
                    throw new InvalidOperationException($"Release {release} is not the album's monitored release; switch the album to it first");
                }
            }

            foreach (var group in requested.GroupBy(t => t.AlbumReleaseId))
            {
                var release = _releaseService.GetRelease(group.Key);
                var album = _albumService.GetAlbum(release.AlbumId);
                var tracks = _trackService.GetTracksByRelease(release.Id);
                var ids = group.Select(t => t.Id).ToHashSet();
                var before = tracks.ToDictionary(t => t.Id, t => t.Monitored);

                if (monitored)
                {
                    if (!album.Monitored)
                    {
                        _logger.Debug("Monitoring {0} for {1} track(s) only", album, ids.Count);
                        _albumService.SetMonitored(new[] { album.Id }, true);
                        tracks.ForEach(t => t.Monitored = ids.Contains(t.Id));
                    }
                    else
                    {
                        tracks.Where(t => ids.Contains(t.Id)).ToList().ForEach(t => t.Monitored = true);
                    }
                }
                else
                {
                    tracks.Where(t => ids.Contains(t.Id)).ToList().ForEach(t => t.Monitored = false);

                    if (tracks.All(t => !t.Monitored))
                    {
                        _logger.Debug("{0} wants no tracks any more, unmonitoring it", album);
                        _albumService.SetMonitored(new[] { album.Id }, false);
                        tracks.ForEach(t => t.Monitored = true);
                    }
                }

                _trackService.UpdateMonitored(tracks.Where(t => t.Monitored != before[t.Id]).ToList());
            }
        }
    }
}
