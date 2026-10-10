using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // Tomvis fork (MUSIC-64): an album can want only some of its tracks; unwanted tracks never count as missing.
    [TestFixture]
    public class TrackMonitoringRepositoryFixture : DbTest<TrackRepository, Track>
    {
        private Artist _artist;
        private Album _album;
        private AlbumRelease _release;
        private List<Track> _tracks;

        [SetUp]
        public void Setup()
        {
            _artist = Db.Insert(Builder<Artist>.CreateNew()
                .With(a => a.Monitored = true)
                .With(a => a.ArtistMetadataId = 1)
                .With(a => a.QualityProfileId = 1)
                .BuildNew());

            _album = Db.Insert(Builder<Album>.CreateNew()
                .With(a => a.ArtistMetadataId = 1)
                .With(a => a.Monitored = true)
                .With(a => a.ReleaseDate = new System.DateTime(2020, 1, 1))
                .BuildNew());

            _release = Db.Insert(Builder<AlbumRelease>.CreateNew()
                .With(r => r.AlbumId = _album.Id)
                .With(r => r.Monitored = true)
                .BuildNew());

            _tracks = Builder<Track>.CreateListOfSize(3)
                .All()
                .With(t => t.Id = 0)
                .With(t => t.AlbumReleaseId = _release.Id)
                .With(t => t.ArtistMetadataId = 1)
                .With(t => t.TrackFileId = 0)
                .With(t => t.Monitored = true)
                .BuildListOfNew();
            Db.InsertMany(_tracks);
            _tracks = Db.All<Track>();
        }

        private void GiveFile(Track track, Quality quality)
        {
            var file = Db.Insert(Builder<TrackFile>.CreateNew()
                .With(f => f.AlbumId = _album.Id)
                .With(f => f.Path = "/music/" + track.Id + ".flac")
                .With(f => f.Quality = new QualityModel(quality))
                .BuildNew());
            track.TrackFileId = file.Id;
            Db.Update(track);
        }

        private void Unmonitor(params Track[] tracks)
        {
            foreach (var track in tracks)
            {
                track.Monitored = false;
                Db.Update(track);
            }
        }

        private List<Album> MissingAlbums()
        {
            var spec = new PagingSpec<Album> { Page = 1, PageSize = 10, SortKey = "Id" };
            return Mocker.Resolve<AlbumRepository>().AlbumsWithoutFiles(spec).Records;
        }

        private List<Album> CutoffUnmetAlbums()
        {
            var spec = new PagingSpec<Album> { Page = 1, PageSize = 10, SortKey = "Id" };
            var belowCutoff = new List<QualitiesBelowCutoff> { new QualitiesBelowCutoff(1, new[] { Quality.MP3_320.Id }) };
            return Mocker.Resolve<AlbumRepository>().AlbumsWhereCutoffUnmet(spec, belowCutoff).Records;
        }

        [Test]
        public void new_tracks_should_be_monitored_by_default()
        {
            new Track().Monitored.Should().BeTrue();
        }

        [Test]
        public void monitored_flag_should_round_trip()
        {
            Unmonitor(_tracks[1]);

            Subject.Get(_tracks[1].Id).Monitored.Should().BeFalse();
            Subject.Get(_tracks[0].Id).Monitored.Should().BeTrue();
        }

        [Test]
        public void tracks_without_files_should_skip_unmonitored_tracks()
        {
            Unmonitor(_tracks[0], _tracks[1]);

            Subject.TracksWithoutFiles(_album.Id).Select(t => t.Id).Should().BeEquivalentTo(new[] { _tracks[2].Id });
        }

        [Test]
        public void album_should_not_be_missing_when_only_unmonitored_tracks_lack_files()
        {
            Unmonitor(_tracks[0], _tracks[1]);
            GiveFile(_tracks[2], Quality.FLAC);

            MissingAlbums().Should().BeEmpty();
        }

        [Test]
        public void album_should_be_missing_when_a_monitored_track_lacks_a_file()
        {
            Unmonitor(_tracks[0], _tracks[1]);

            MissingAlbums().Select(a => a.Id).Should().BeEquivalentTo(new[] { _album.Id });
        }

        [Test]
        public void cutoff_should_ignore_files_of_unmonitored_tracks()
        {
            Unmonitor(_tracks[0]);
            GiveFile(_tracks[0], Quality.MP3_320);
            GiveFile(_tracks[1], Quality.FLAC);
            GiveFile(_tracks[2], Quality.FLAC);

            CutoffUnmetAlbums().Should().BeEmpty();
        }

        [Test]
        public void cutoff_should_count_files_of_monitored_tracks()
        {
            Unmonitor(_tracks[0]);
            GiveFile(_tracks[1], Quality.MP3_320);

            CutoffUnmetAlbums().Select(a => a.Id).Should().BeEquivalentTo(new[] { _album.Id });
        }
    }
}
