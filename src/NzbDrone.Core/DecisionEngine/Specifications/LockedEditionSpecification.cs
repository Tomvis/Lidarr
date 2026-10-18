using System.Linq;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Tomvis fork (MUSIC-135): the bestedition pipeline swaps an album to its best-DR (or merged) edition and locks it.
    // Custom-format upgrades (24bit, Remaster, Special Edition) would replace those files without the edition judge, so
    // a locked edition is not upgraded; only tracks it is missing may be grabbed. A held edition is unaffected.
    public class LockedEditionSpecification : IDecisionEngineSpecification
    {
        private readonly ITrackService _trackService;
        private readonly Logger _logger;

        public LockedEditionSpecification(ITrackService trackService, Logger logger)
        {
            _trackService = trackService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria)
        {
            foreach (var album in subject.Albums)
            {
                var releases = album.AlbumReleases?.Value;

                if (releases == null || !releases.Any(r => r.Locked))
                {
                    continue;
                }

                var edition = AlbumEditions.IsMultiEdition(album)
                    ? AlbumEditions.TargetEdition(album, subject.Release?.Title)
                    : AlbumEditions.Monitored(releases).FirstOrDefault();

                if (edition == null || !edition.Locked || AlbumEditions.IsHeld(album, edition))
                {
                    continue;
                }

                if (_trackService.GetTracksByRelease(edition.Id).Any(t => t.Monitored && t.TrackFileId == 0))
                {
                    continue;
                }

                _logger.Debug("{0} is for {1}'s locked edition", subject.Release?.Title, album);
                return Decision.Reject("{0}'s edition is locked: not upgraded", album.Title);
            }

            return Decision.Accept();
        }
    }
}
