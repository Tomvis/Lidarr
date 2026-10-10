using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class ReleaseWantedSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private readonly Logger _logger;

        public ReleaseWantedSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            // Tomvis fork (MUSIC-63): with several editions monitored, AnyReleaseOk can't pull in another release.
            // Tomvis fork (MUSIC-64): wanted tracks live on the monitored release, so another release can't stand in.
            var album = item.AlbumRelease.Album.Value;
            if (item.AlbumRelease.Monitored || (album.AnyReleaseOk && !AlbumEditions.IsMultiEdition(album) && !WantedTracks.IsPartial(album)))
            {
                return Decision.Accept();
            }

            _logger.Debug("AlbumRelease {0} was not requested", item);
            return Decision.Reject("Album release not requested");
        }
    }
}
