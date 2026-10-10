using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class WebhookAlbumRelease
    {
        public WebhookAlbumRelease()
        {
        }

        public WebhookAlbumRelease(AlbumRelease release)
        {
            Id = release.Id;
            MBId = release.ForeignReleaseId;
            Title = release.Title;
            Disambiguation = release.Disambiguation;
            Country = release.Country;
            Format = AlbumEditions.FormatMedia(release.Media);
            TrackCount = release.TrackCount;
            Status = release.Status;
            ReleaseDate = release.ReleaseDate;

            // Tomvis fork (MUSIC-64): only when the release wants some of its tracks
            WantedTracks = NzbDrone.Core.Music.WantedTracks.IsPartial(release)
                ? release.Tracks.Value.Where(t => t.Monitored).Select(t => new WebhookTrack(t)).ToList()
                : null;
        }

        public int Id { get; set; }
        public string MBId { get; set; }
        public string Title { get; set; }
        public string Disambiguation { get; set; }
        public List<string> Country { get; set; }
        public string Format { get; set; }
        public int TrackCount { get; set; }
        public string Status { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public List<WebhookTrack> WantedTracks { get; set; }
    }
}
