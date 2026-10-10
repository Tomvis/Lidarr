using NzbDrone.Core.Music;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class WebhookTrack
    {
        public WebhookTrack()
        {
        }

        public WebhookTrack(Track track)
        {
            Id = track.Id;
            Title = track.Title;
            TrackNumber = track.TrackNumber;
            MBId = track.ForeignTrackId;
            RecordingMBId = track.ForeignRecordingId;
            MediumNumber = track.MediumNumber;
            AbsoluteTrackNumber = track.AbsoluteTrackNumber;
        }

        public int Id { get; set; }
        public string Title { get; set; }
        public string TrackNumber { get; set; }

        // Tomvis fork (MUSIC-64)
        public string MBId { get; set; }
        public string RecordingMBId { get; set; }
        public int MediumNumber { get; set; }
        public int AbsoluteTrackNumber { get; set; }

        public string Quality { get; set; }
        public int QualityVersion { get; set; }
        public string ReleaseGroup { get; set; }
    }
}
