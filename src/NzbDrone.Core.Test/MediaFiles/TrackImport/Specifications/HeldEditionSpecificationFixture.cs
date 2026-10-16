using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.DecisionEngineTests;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Specifications
{
    // Tomvis fork (MUSIC-95): a held edition is imported only by a ManualImport pinned to it (the edition judge's pass)
    [TestFixture]
    public class HeldEditionSpecificationFixture : CoreTest<HeldEditionSpecification>
    {
        private Album _album;
        private AlbumRelease _owned;
        private AlbumRelease _held;

        [SetUp]
        public void Setup()
        {
            _album = EditionTestData.Remaster();
            _owned = _album.AlbumReleases.Value[0];
            _held = _album.AlbumReleases.Value[1];
            _album.AlbumReleases.Value.ForEach(r => r.Album = _album);
        }

        private LocalAlbumRelease Importing(AlbumRelease release, bool newDownload = true)
        {
            return new LocalAlbumRelease(new List<LocalTrack>()) { AlbumRelease = release, NewDownload = newDownload };
        }

        private DownloadClientItem Download(string clientTitle, string grabTitle = null)
        {
            var history = grabTitle == null
                ? new List<EntityHistory>()
                : new List<EntityHistory> { new EntityHistory { EventType = EntityHistoryEventType.Grabbed, SourceTitle = grabTitle } };

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.Find("dl1", EntityHistoryEventType.Grabbed))
                .Returns(history);

            return new DownloadClientItem { DownloadId = "dl1", Title = clientTitle };
        }

        [Test]
        public void should_reject_files_identified_as_a_held_edition()
        {
            Subject.IsSatisfiedBy(Importing(_held), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_download_grabbed_for_a_held_edition_even_if_identified_as_the_owned_one()
        {
            var download = Download("Album.FLAC", "Artist - Album (1995) [Japan] FLAC");

            Subject.IsSatisfiedBy(Importing(_owned), download).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_fall_back_to_the_client_title_without_a_grab()
        {
            var download = Download("Artist - Album (1995) [Japan] FLAC");

            Subject.IsSatisfiedBy(Importing(_owned), download).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_download_grabbed_for_the_owned_edition()
        {
            var download = Download("Artist - Album (1995) [Japan] FLAC", "Artist - Album (2012 Remaster) [FLAC]");

            Subject.IsSatisfiedBy(Importing(_owned), download).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_files_already_in_the_library()
        {
            Subject.IsSatisfiedBy(Importing(_held, false), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_held_edition_once_it_is_no_longer_held()
        {
            _held.Held = false;

            Subject.IsSatisfiedBy(Importing(_held), Download("Artist - Album (1995) [Japan] FLAC")).Accepted.Should().BeTrue();
        }
    }
}
