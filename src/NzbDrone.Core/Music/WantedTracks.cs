using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Music
{
    // Tomvis fork (MUSIC-64): an album can want only some tracks of its monitored release
    public static class WantedTracks
    {
        public static bool IsPartial(AlbumRelease release)
        {
            return release.Tracks?.Value?.Any(t => !t.Monitored) ?? false;
        }

        public static bool IsPartial(Album album)
        {
            return AlbumEditions.Monitored(album?.AlbumReleases?.Value).Any(IsPartial);
        }

        // The files an upgrade or cutoff check should judge: only those of wanted tracks
        public static List<TrackFile> WantedFiles(Album album, List<TrackFile> files)
        {
            if (!IsPartial(album))
            {
                return files;
            }

            var wanted = AlbumEditions.Monitored(album.AlbumReleases.Value)
                .SelectMany(r => r.Tracks.Value)
                .Where(t => t.Monitored && t.HasFile)
                .Select(t => t.TrackFileId)
                .ToHashSet();

            return files.Where(f => wanted.Contains(f.Id)).ToList();
        }

        // Tracks a download of this release should bring
        public static int Count(AlbumRelease release)
        {
            return IsPartial(release) ? release.Tracks.Value.Count(t => t.Monitored) : release.TrackCount;
        }
    }
}
