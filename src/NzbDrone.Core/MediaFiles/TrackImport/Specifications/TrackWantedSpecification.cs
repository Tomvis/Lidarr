using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    // Tomvis fork (MUSIC-64): a download imports only the tracks the album wants
    public class TrackWantedSpecification : IImportDecisionEngineSpecification<LocalTrack>
    {
        private readonly Logger _logger;

        public TrackWantedSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalTrack item, DownloadClientItem downloadClientItem)
        {
            if (!item.ExistingFile && item.Tracks.Any() && item.Tracks.All(t => !t.Monitored))
            {
                _logger.Debug("Track is not wanted. Skipping {0}", item);
                return Decision.Reject("Track not wanted");
            }

            return Decision.Accept();
        }
    }
}
