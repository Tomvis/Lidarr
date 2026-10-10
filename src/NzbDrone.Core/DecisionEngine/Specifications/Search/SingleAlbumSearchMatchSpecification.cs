using System.Linq;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.Search
{
    public class SingleAlbumSearchMatchSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public SingleAlbumSearchMatchSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteAlbum remoteAlbum, SearchCriteriaBase searchCriteria)
        {
            if (searchCriteria == null)
            {
                return Decision.Accept();
            }

            var singleAlbumSpec = searchCriteria as AlbumSearchCriteria;
            if (singleAlbumSpec == null)
            {
                return Decision.Accept();
            }

            if (Parser.Parser.CleanArtistName(singleAlbumSpec.AlbumTitle) != Parser.Parser.CleanArtistName(remoteAlbum.ParsedAlbumInfo.AlbumTitle) &&
                !IsMonitoredEditionTitle(singleAlbumSpec, remoteAlbum.ParsedAlbumInfo.AlbumTitle))
            {
                _logger.Debug("Album does not match searched album title, skipping.");
                return Decision.Reject("Wrong album");
            }

            if (!remoteAlbum.ParsedAlbumInfo.AlbumTitle.Any())
            {
                _logger.Debug("Full discography result during single album search, skipping.");
                return Decision.Reject("Full artist pack");
            }

            return Decision.Accept();
        }

        // Tomvis fork (MUSIC-63): "Ihsahn (Orchestral Version)" is the searched album when that edition is monitored
        private static bool IsMonitoredEditionTitle(AlbumSearchCriteria criteria, string parsedTitle)
        {
            var album = criteria.Albums?.FirstOrDefault();

            if (album == null || !AlbumEditions.IsMultiEdition(album))
            {
                return false;
            }

            var parsed = Parser.Parser.CleanArtistName(parsedTitle);

            return AlbumEditions.Monitored(album.AlbumReleases.Value)
                .Any(r => Parser.Parser.CleanArtistName(AlbumEditions.EditionTitle(album, r)) == parsed);
        }
    }
}
