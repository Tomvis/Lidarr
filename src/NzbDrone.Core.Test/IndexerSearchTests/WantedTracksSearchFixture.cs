using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    // Tomvis fork (MUSIC-64): an album search tells indexers which tracks the album still wants
    [TestFixture]
    public class WantedTracksSearchFixture : CoreTest<ReleaseSearchService>
    {
        private Album _album;
        private List<Track> _tracks;

        [SetUp]
        public void Setup()
        {
            _tracks = Enumerable.Range(1, 3).Select(i => new Track { Id = i, Monitored = true }).ToList();
            _album = new Album
            {
                Id = 1,
                Title = "Album",
                ArtistId = 1,
                AlbumReleases = new List<AlbumRelease> { new AlbumRelease { Id = 1, Monitored = true, Tracks = _tracks } }
            };

            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtist(1)).Returns(new Artist { Id = 1, Tags = new HashSet<int>() });
            Mocker.GetMock<IIndexerFactory>().Setup(s => s.AutomaticSearchEnabled(It.IsAny<bool>())).Returns(new List<IIndexer>());
            Mocker.GetMock<IMakeDownloadDecision>()
                .Setup(s => s.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<SearchCriteriaBase>()))
                .Returns(new List<DownloadDecision>());
        }

        [Test]
        public void should_not_list_tracks_when_album_wants_every_track()
        {
            Subject.AlbumSearch(_album, false, false, false).GetAwaiter().GetResult();

            Mocker.GetMock<IMakeDownloadDecision>()
                .Verify(v => v.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.Is<AlbumSearchCriteria>(c => c.Tracks == null)), Times.Once());
        }

        [Test]
        public void should_list_wanted_tracks_without_files()
        {
            _tracks[0].Monitored = false;
            _tracks[1].TrackFileId = 5;

            Subject.AlbumSearch(_album, false, false, false).GetAwaiter().GetResult();

            Mocker.GetMock<IMakeDownloadDecision>()
                .Verify(v => v.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.Is<AlbumSearchCriteria>(c => c.Tracks.Single().Id == 3)), Times.Once());
        }
    }
}
