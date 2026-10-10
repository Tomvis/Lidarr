using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Tomvis fork (MUSIC-63): with several editions monitored, a result naming an edition that isn't one of them
    // ("Instrumentals" when only main + orchestral are monitored) would be imported as one of the monitored ones
    public class EditionSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public EditionSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria)
        {
            foreach (var album in subject.Albums)
            {
                if (AlbumEditions.IsMultiEdition(album) && AlbumEditions.TargetEdition(album, subject.Release?.Title) == null)
                {
                    _logger.Debug("{0} is an unmonitored edition of {1}", subject.Release?.Title, album);
                    return Decision.Reject("Release is an unmonitored edition of {0}", album.Title);
                }
            }

            return Decision.Accept();
        }
    }
}
