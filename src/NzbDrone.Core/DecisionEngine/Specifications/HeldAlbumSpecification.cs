using System.Linq;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Tomvis fork (MUSIC-103): a held edition is searched like any missing one, and those searches also find copies of the
    // owned edition. Taken as custom-format upgrades, they replaced the owned files without the edition judge (10-17,
    // 10 albums). While an album holds an edition, only that edition or tracks the album is missing may be grabbed.
    public class HeldAlbumSpecification : IDecisionEngineSpecification
    {
        private readonly ITrackService _trackService;
        private readonly Logger _logger;

        public HeldAlbumSpecification(ITrackService trackService, Logger logger)
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

                if (releases == null || !releases.Any(r => AlbumEditions.IsHeld(album, r)))
                {
                    continue;
                }

                var target = AlbumEditions.TargetEdition(album, subject.Release?.Title);

                if (target == null)
                {
                    continue;
                }

                // Tomvis fork (MUSIC-174): a held edition is matched by release data alone (often just a year), and upstream's
                // search match takes "The Essential Elvis Presley (2006)" for the self-titled album. A grab for it must name it.
                if (AlbumEditions.IsHeld(album, target))
                {
                    if (AlbumEditions.NamesAlbum(album, subject.Artist?.Name, subject.Release?.Title))
                    {
                        continue;
                    }

                    _logger.Debug("{0} does not name {1}, held edition {2} not grabbed", subject.Release?.Title, album, target);
                    return Decision.Reject("{0} holds an edition: the result does not name the album", album.Title);
                }

                if (_trackService.GetTracksByRelease(target.Id).Any(t => t.Monitored && t.TrackFileId == 0))
                {
                    continue;
                }

                _logger.Debug("{0} is for {1}'s owned edition while another is held", subject.Release?.Title, album);
                return Decision.Reject("{0} holds an edition: the owned edition is not upgraded", album.Title);
            }

            return Decision.Accept();
        }
    }
}
