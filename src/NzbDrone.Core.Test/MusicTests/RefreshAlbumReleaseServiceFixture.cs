using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshAlbumReleaseServiceFixture : CoreTest<RefreshAlbumReleaseService>
    {
        private AlbumRelease _release;
        private List<Track> _tracks;
        private ArtistMetadata _metadata;

        [SetUp]
        public void Setup()
        {
            _release = Builder<AlbumRelease>
                .CreateNew()
                .With(s => s.Media = new List<Medium> { new Medium { Number = 1 } })
                .With(s => s.ForeignReleaseId = "xxx-xxx-xxx-xxx")
                .With(s => s.Monitored = true)
                .With(s => s.TrackCount = 10)
                .Build();

            _metadata = Builder<ArtistMetadata>.CreateNew().Build();

            _tracks = Builder<Track>
                .CreateListOfSize(10)
                .All()
                .With(x => x.AlbumReleaseId = _release.Id)
                .With(x => x.ArtistMetadata = _metadata)
                .With(x => x.ArtistMetadataId = _metadata.Id)
                .BuildList();

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksForRefresh(_release.Id, It.IsAny<List<string>>()))
                .Returns(_tracks);

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(It.IsAny<int>()))
                .Returns(new List<Track>());
        }

        [Test]
        public void should_update_if_musicbrainz_id_changed_and_no_clash()
        {
            var newInfo = _release.JsonClone();
            newInfo.ForeignReleaseId = _release.ForeignReleaseId + 1;
            newInfo.OldForeignReleaseIds = new List<string> { _release.ForeignReleaseId };
            newInfo.Tracks = _tracks;

            Subject.RefreshEntityInfo(_release, new List<AlbumRelease> { newInfo }, false, false, null);

            Mocker.GetMock<IReleaseService>()
                .Verify(v => v.UpdateMany(It.Is<List<AlbumRelease>>(s => s.First().ForeignReleaseId == newInfo.ForeignReleaseId)));
        }

        [Test]
        public void should_merge_if_musicbrainz_id_changed_and_new_already_exists()
        {
            var existing = _release;

            var clash = existing.JsonClone();
            clash.Id = 100;
            clash.ForeignReleaseId = clash.ForeignReleaseId + 1;

            clash.Tracks = Builder<Track>.CreateListOfSize(10)
                .All()
                .With(x => x.AlbumReleaseId = clash.Id)
                .With(x => x.ArtistMetadata = _metadata)
                .With(x => x.ArtistMetadataId = _metadata.Id)
                .BuildList();

            Mocker.GetMock<IReleaseService>()
                .Setup(x => x.GetReleaseByForeignReleaseId(clash.ForeignReleaseId, false))
                .Returns(clash);

            Mocker.GetMock<ITrackService>()
                .Setup(x => x.GetTracksForRefresh(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns(_tracks);

            var newInfo = existing.JsonClone();
            newInfo.ForeignReleaseId = _release.ForeignReleaseId + 1;
            newInfo.OldForeignReleaseIds = new List<string> { _release.ForeignReleaseId };
            newInfo.Tracks = _tracks;

            Subject.RefreshEntityInfo(new List<AlbumRelease> { clash, existing }, new List<AlbumRelease> { newInfo }, false, false);

            // check old album is deleted
            Mocker.GetMock<IReleaseService>()
                .Verify(v => v.DeleteMany(It.Is<List<AlbumRelease>>(x => x.First().ForeignReleaseId == existing.ForeignReleaseId)));

            // check that clash gets updated
            Mocker.GetMock<IReleaseService>()
                .Verify(v => v.UpdateMany(It.Is<List<AlbumRelease>>(s => s.First().ForeignReleaseId == newInfo.ForeignReleaseId)));
        }

        [Test]
        public void child_merge_targets_should_not_be_null_if_target_is_new()
        {
            var oldTrack = Builder<Track>
                .CreateNew()
                .With(x => x.AlbumReleaseId = _release.Id)
                .With(x => x.ArtistMetadata = _metadata)
                .With(x => x.ArtistMetadataId = _metadata.Id)
                .Build();
            _release.Tracks = new List<Track> { oldTrack };

            var newInfo = _release.JsonClone();
            var newTrack = oldTrack.JsonClone();
            newTrack.ArtistMetadata = _metadata;
            newTrack.ArtistMetadataId = _metadata.Id;
            newTrack.ForeignTrackId = "new id";
            newTrack.OldForeignTrackIds = new List<string> { oldTrack.ForeignTrackId };
            newInfo.Tracks = new List<Track> { newTrack };

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksForRefresh(_release.Id, It.IsAny<List<string>>()))
                .Returns(new List<Track> { oldTrack });

            Subject.RefreshEntityInfo(_release, new List<AlbumRelease> { newInfo }, false, false, null);

            Mocker.GetMock<IRefreshTrackService>()
                .Verify(v => v.RefreshTrackInfo(It.IsAny<List<Track>>(),
                                                It.IsAny<List<Track>>(),
                                                It.Is<List<Tuple<Track, Track>>>(x => x.All(y => y.Item2 != null)),
                                                It.IsAny<List<Track>>(),
                                                It.IsAny<List<Track>>(),
                                                It.IsAny<List<Track>>(),
                                                It.IsAny<bool>()));

            Mocker.GetMock<IReleaseService>()
                .Verify(v => v.UpdateMany(It.Is<List<AlbumRelease>>(s => s.First().ForeignReleaseId == newInfo.ForeignReleaseId)));
        }

        // Tomvis fork (MUSIC-64): a track MusicBrainz adds to a release that wants only some tracks is not wanted
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void new_track_should_be_wanted_only_if_release_wants_every_track(bool allWanted, bool expected)
        {
            _tracks.ForEach(x => x.Monitored = true);
            _tracks[0].Monitored = allWanted;

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(_release.Id))
                .Returns(_tracks);

            var newTrack = Builder<Track>.CreateNew()
                .With(x => x.Id = 0)
                .With(x => x.ForeignTrackId = "added")
                .With(x => x.OldForeignTrackIds = new List<string>())
                .With(x => x.ArtistMetadata = _metadata)
                .With(x => x.Monitored = true)
                .Build();

            var newInfo = _release.JsonClone();
            newInfo.Tracks = _tracks.Select(x => x.JsonClone()).Concat(new[] { newTrack }).ToList();
            newInfo.Tracks.Value.ForEach(x => x.ArtistMetadata = _metadata);

            Subject.RefreshEntityInfo(_release, new List<AlbumRelease> { newInfo }, false, false, null);

            Mocker.GetMock<IRefreshTrackService>()
                .Verify(v => v.RefreshTrackInfo(It.Is<List<Track>>(x => x.Single().ForeignTrackId == "added" && x.Single().Monitored == expected),
                                                It.IsAny<List<Track>>(),
                                                It.IsAny<List<Tuple<Track, Track>>>(),
                                                It.IsAny<List<Track>>(),
                                                It.IsAny<List<Track>>(),
                                                It.IsAny<List<Track>>(),
                                                It.IsAny<bool>()));
        }
    }
}
