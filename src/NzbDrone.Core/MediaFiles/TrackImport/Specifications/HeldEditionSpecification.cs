using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    // Tomvis fork (MUSIC-95): a held edition is a candidate awaiting the edition judge, so nothing imports it on its own.
    // The judge's pass is a ManualImport pinned to the held release (ManualImport skips these specs); a fail
    // removes the download from the queue. Rejects new files identified as a held edition, and a download grabbed
    // for one whatever identification picked (a same-tracklist mastering often identifies as the owned edition).
    public class HeldEditionSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public HeldEditionSpecification(IHistoryService historyService, Logger logger)
        {
            _historyService = historyService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            if (!item.NewDownload)
            {
                return Decision.Accept();
            }

            var album = item.AlbumRelease.Album.Value;

            if (AlbumEditions.IsHeld(album, item.AlbumRelease))
            {
                _logger.Debug("{0} is a held edition, awaiting edition judge", item);
                return Decision.Reject("Held edition: awaiting edition judge");
            }

            if (downloadClientItem == null)
            {
                return Decision.Accept();
            }

            var grabTitle = _historyService.Find(downloadClientItem.DownloadId, EntityHistoryEventType.Grabbed)
                .FirstOrDefault()?.SourceTitle ?? downloadClientItem.Title;
            var target = AlbumEditions.TargetEdition(album, grabTitle);

            if (AlbumEditions.IsHeld(album, target))
            {
                _logger.Debug("{0} was grabbed for held edition {1}, awaiting edition judge", grabTitle, target);
                return Decision.Reject("Grabbed for a held edition: awaiting edition judge");
            }

            return Decision.Accept();
        }
    }
}
