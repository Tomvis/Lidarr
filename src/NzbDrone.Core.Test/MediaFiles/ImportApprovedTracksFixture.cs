using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ImportApprovedTracksFixture : CoreTest<ImportApprovedTracks>
    {
        private List<ImportDecision<LocalTrack>> _rejectedDecisions;
        private List<ImportDecision<LocalTrack>> _approvedDecisions;

        private DownloadClientItem _downloadClientItem;
        private DownloadClientItemClientInfo _clientInfo;

        [SetUp]
        public void Setup()
        {
            _rejectedDecisions = new List<ImportDecision<LocalTrack>>();
            _approvedDecisions = new List<ImportDecision<LocalTrack>>();

            var artist = Builder<Artist>.CreateNew()
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .With(s => s.Path = @"C:\Test\Music\Alien Ant Farm".AsOsAgnostic())
                                        .Build();

            var album = Builder<Album>.CreateNew()
                .With(e => e.Artist = artist)
                .Build();

            var release = Builder<AlbumRelease>.CreateNew()
                .With(e => e.AlbumId = album.Id)
                .With(e => e.Monitored = true)
                .Build();

            album.AlbumReleases = new List<AlbumRelease> { release };

            var tracks = Builder<Track>.CreateListOfSize(5)
                                           .Build();

            _rejectedDecisions.Add(new ImportDecision<LocalTrack>(new LocalTrack(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalTrack>(new LocalTrack(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalTrack>(new LocalTrack(), new Rejection("Rejected!")));

            foreach (var track in tracks)
            {
                _approvedDecisions.Add(new ImportDecision<LocalTrack>(
                                           new LocalTrack
                                           {
                                               Artist = artist,
                                               Album = album,
                                               Release = release,
                                               Tracks = new List<Track> { track },
                                               Path = Path.Combine(artist.Path, "Alien Ant Farm - 01 - Pilot.mp3"),
                                               Quality = new QualityModel(Quality.MP3_256),
                                               FileTrackInfo = new ParsedTrackInfo
                                               {
                                                   ReleaseGroup = "DRONE"
                                               }
                                           }));
            }

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns(new TrackFileMoveResult());

            _clientInfo = Builder<DownloadClientItemClientInfo>.CreateNew().Build();
            _downloadClientItem = Builder<DownloadClientItem>.CreateNew().With(x => x.DownloadClientInfo = _clientInfo).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAlbum(It.IsAny<int>()))
                .Returns(new List<TrackFile>());

            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByFileId(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Track>());
        }

        [Test]
        public void should_not_import_any_if_there_are_no_approved_decisions()
        {
            Subject.Import(_rejectedDecisions, false).Where(i => i.Result == ImportResultType.Imported).Should().BeEmpty();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<TrackFile>()), Times.Never());
        }

        [Test]
        public void should_import_each_approved()
        {
            Subject.Import(_approvedDecisions, false).Should().HaveCount(5);
        }

        [Test]
        public void should_only_import_approved()
        {
            var all = new List<ImportDecision<LocalTrack>>();
            all.AddRange(_rejectedDecisions);
            all.AddRange(_approvedDecisions);

            var result = Subject.Import(all, false);

            result.Should().HaveCount(all.Count);
            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_only_import_each_track_once()
        {
            var all = new List<ImportDecision<LocalTrack>>();
            all.AddRange(_approvedDecisions);
            all.Add(new ImportDecision<LocalTrack>(_approvedDecisions.First().Item));

            var result = Subject.Import(all, false);

            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_move_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, false),
                          Times.Once());
        }

        [Test]
        public void should_publish_TrackImportedEvent_for_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Once());
        }

        [Test]
        public void should_publish_new_download_AlbumImportedEvent_for_files_from_outside_the_library_without_replacing()
        {
            GivenAlbum();
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, false);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<AlbumImportedEvent>(e => e.NewDownload)), Times.Once());
        }

        [Test]
        public void should_not_publish_new_download_AlbumImportedEvent_for_existing_files()
        {
            GivenAlbum();
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<AlbumImportedEvent>(e => !e.NewDownload)), Times.Once());
        }

        private void GivenAlbum()
        {
            var album = _approvedDecisions.First().Item.Album;

            Mocker.GetMock<IAlbumService>()
                .Setup(s => s.GetAlbum(album.Id))
                .Returns(album);
        }

        [Test]
        public void should_not_move_existing_files()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, false),
                          Times.Never());
        }

        [Test]
        public void should_import_larger_files_first()
        {
            var fileDecision = _approvedDecisions.First();
            fileDecision.Item.Size = 1.Gigabytes();

            var sampleDecision = new ImportDecision<LocalTrack>(
                new LocalTrack
                {
                    Artist = fileDecision.Item.Artist,
                    Album = fileDecision.Item.Album,
                    Tracks = new List<Track> { fileDecision.Item.Tracks.First() },
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.MP3_256),
                    Size = 80.Megabytes()
                });

            var all = new List<ImportDecision<LocalTrack>>();
            all.Add(fileDecision);
            all.Add(sampleDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == fileDecision.Item.Size);
        }

        [Test]
        public void should_copy_when_cannot_move_files_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo });

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, true), Times.Once());
        }

        [Test]
        public void should_use_override_importmode()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo }, ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, false), Times.Once());
        }

        [Test]
        public void should_reuse_existing_trackfile_with_the_same_path()
        {
            var previousFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 42).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(previousFile);

            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            // deleting the old row mid-loop and inserting the new one after the loop loses
            // every row deleted so far when the import is interrupted
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Update(It.Is<List<TrackFile>>(l => l.Count == 1 && l[0].Id == 42 && l[0].Path == track.Item.Path)), Times.Once());
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.AddMany(It.Is<List<TrackFile>>(l => l.Count == 0)), Times.Once());
        }

        [Test]
        public void should_link_tracks_to_reused_trackfile_and_unlink_tracks_it_no_longer_matches()
        {
            var previousFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 42).Build();
            var staleTrack = Builder<Track>.CreateNew().With(t => t.Id = 999).With(t => t.TrackFileId = 42).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(previousFile);
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByFileId(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Track> { staleTrack });

            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            var mappedTrack = track.Item.Tracks.Single();

            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            Mocker.GetMock<ITrackService>()
                .Verify(v => v.SetFileIds(It.Is<List<Track>>(l => l.Count == 2 &&
                                                                    l.Any(t => t.Id == mappedTrack.Id && t.TrackFileId == 42) &&
                                                                    l.Any(t => t.Id == 999 && t.TrackFileId == 0))),
                        Times.Once());
        }

        [Test]
        public void should_import_duplicate_path_only_once_when_reusing_trackfile()
        {
            var previousFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 42).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(previousFile);

            var first = _approvedDecisions[0];
            var second = _approvedDecisions[1];
            first.Item.ExistingFile = true;
            second.Item.ExistingFile = true;
            second.Item.Path = first.Item.Path;

            var results = Subject.Import(new List<ImportDecision<LocalTrack>> { first, second }, false);

            results.Where(r => r.Result == ImportResultType.Imported).Should().HaveCount(1);
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Update(It.Is<List<TrackFile>>(l => l.Count == 1)), Times.Once());
        }

        [Test]
        public void should_keep_download_provenance_when_reusing_trackfile()
        {
            var previousFile = Builder<TrackFile>.CreateNew()
                .With(f => f.Id = 42)
                .With(f => f.SceneName = "Artist - Album (2014) [FLAC 24bit] [WEB]")
                .With(f => f.OriginalFilePath = "Artist - Album (2014) [FLAC 24bit] [WEB]/01 - Track.flac")
                .With(f => f.ReleaseGroup = "ENRiCH")
                .With(f => f.IndexerFlags = IndexerFlags.Freeleech)
                .Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(previousFile);

            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            track.Item.ReleaseGroup = null;
            track.Item.IndexerFlags = 0;

            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            // a rescan after a retag builds the file from disk alone; without the release name its
            // custom formats score from the bare filename and any search finds an "upgrade"
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Update(It.Is<List<TrackFile>>(l => l.Count == 1 &&
                                                                    l[0].SceneName == previousFile.SceneName &&
                                                                    l[0].OriginalFilePath == previousFile.OriginalFilePath &&
                                                                    l[0].ReleaseGroup == "ENRiCH" &&
                                                                    l[0].IndexerFlags == IndexerFlags.Freeleech)),
                        Times.Once());
        }

        [Test]
        public void should_prefer_rescanned_release_group_when_reusing_trackfile()
        {
            var previousFile = Builder<TrackFile>.CreateNew()
                .With(f => f.Id = 42)
                .With(f => f.ReleaseGroup = "OLD")
                .Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(previousFile);

            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            track.Item.ReleaseGroup = "NEW";

            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Update(It.Is<List<TrackFile>>(l => l.Count == 1 && l[0].ReleaseGroup == "NEW")), Times.Once());
        }

        [Test]
        public void should_include_scene_name_with_new_downloads()
        {
            var firstDecision = _approvedDecisions.First();
            firstDecision.Item.SceneName = "Artist.Name.Album.Name.TrackNum.Track.Title.MP3256";

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                .Verify(v => v.UpgradeTrackFile(It.Is<TrackFile>(e => e.SceneName == firstDecision.Item.SceneName), _approvedDecisions.First().Item, false),
                    Times.Once());
        }

        private AlbumRelease GivenSecondMonitoredEdition()
        {
            var item = _approvedDecisions.First().Item;
            item.Release.Id = 1;

            var extra = Builder<AlbumRelease>.CreateNew()
                .With(e => e.Id = 2)
                .With(e => e.AlbumId = item.Album.Id)
                .With(e => e.Monitored = true)
                .Build();

            item.Album.AlbumReleases = new List<AlbumRelease> { item.Release, extra };

            return extra;
        }

        private void GivenFileTracks(params Track[] tracks)
        {
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByFileId(It.IsAny<IEnumerable<int>>()))
                .Returns(tracks.ToList());
        }

        private void GivenReleaseTracks(int releaseId, params Track[] tracks)
        {
            Mocker.GetMock<ITrackService>()
                .Setup(s => s.GetTracksByRelease(releaseId))
                .Returns(tracks.ToList());
        }

        private void GivenExistingFiles(TrackFile editionFile, TrackFile otherEditionFile = null)
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAlbum(It.IsAny<int>()))
                .Returns(new[] { editionFile, otherEditionFile }.Where(f => f != null).ToList());

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByRelease(1))
                .Returns(new List<TrackFile> { editionFile });

            var artistPath = _approvedDecisions.First().Item.Artist.Path;
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetParentFolder(It.IsAny<string>()))
                .Returns<string>(p => p == artistPath ? @"C:\Test\Music".AsOsAgnostic() : Path.Combine(artistPath, "Album"));
        }

        [Test]
        public void should_recycle_files_on_no_track_when_one_release_is_monitored()
        {
            var editionFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 10).Build();
            var otherFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 11).Build();
            _approvedDecisions.First().Item.Release.Id = 1;
            GivenExistingFiles(editionFile, otherFile);

            Subject.Import(_approvedDecisions, true);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.Is<TrackFile>(f => f.Id == 10 || f.Id == 11), DeleteMediaFileReason.Upgrade), Times.Exactly(2));
        }

        // Tomvis fork (MUSIC-96): a grab missing some tracks of the monitored release must not recycle their files
        [Test]
        public void should_keep_files_of_tracks_the_import_does_not_bring()
        {
            var keptFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 10).Build();
            var replacedFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 11).Build();
            _approvedDecisions.First().Item.Release.Id = 1;
            GivenExistingFiles(keptFile, replacedFile);
            GivenFileTracks(
                new Track { Id = 100, AlbumReleaseId = 1, TrackFileId = 10, ForeignRecordingId = "rec-missing" },
                new Track { Id = 1, AlbumReleaseId = 1, TrackFileId = 11, ForeignRecordingId = "rec-1" });

            Subject.Import(_approvedDecisions, true);

            // file 11 is left to UpgradeTrackFile (mocked here), which recycles it as its track is imported
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_move_a_file_to_the_new_release_when_the_import_does_not_bring_its_recording()
        {
            var keptFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 10).Build();
            var oldTrack = new Track { Id = 200, AlbumReleaseId = 2, TrackFileId = 10, ForeignRecordingId = "rec-missing" };
            var newTrack = new Track { Id = 100, AlbumReleaseId = 1, TrackFileId = 0, ForeignRecordingId = "rec-missing" };
            _approvedDecisions.First().Item.Release.Id = 1;
            GivenExistingFiles(keptFile);
            GivenFileTracks(oldTrack);
            GivenReleaseTracks(1, newTrack);

            Subject.Import(_approvedDecisions, true);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<ITrackService>()
                .Verify(v => v.SetFileIds(It.Is<List<Track>>(l => l.Count == 2 &&
                                                                    l.Any(t => t.Id == 100 && t.TrackFileId == 10) &&
                                                                    l.Any(t => t.Id == 200 && t.TrackFileId == 0))),
                        Times.Once());
        }

        [Test]
        public void should_recycle_a_file_of_the_old_release_when_the_import_brings_its_recording()
        {
            var replacedFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 10).Build();
            var importedTrack = _approvedDecisions.First().Item.Tracks.Single();
            _approvedDecisions.First().Item.Release.Id = 1;
            GivenExistingFiles(replacedFile);
            GivenFileTracks(new Track { Id = 200, AlbumReleaseId = 2, TrackFileId = 10, ForeignRecordingId = "rec-1" });
            GivenReleaseTracks(1, new Track { Id = importedTrack.Id, AlbumReleaseId = 1, ForeignRecordingId = "rec-1" });

            Subject.Import(_approvedDecisions, true);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.Is<TrackFile>(f => f.Id == 10), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_recycle_a_file_of_the_old_release_when_the_new_release_lacks_its_recording()
        {
            var droppedFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 10).Build();
            _approvedDecisions.First().Item.Release.Id = 1;
            GivenExistingFiles(droppedFile);
            GivenFileTracks(new Track { Id = 200, AlbumReleaseId = 2, TrackFileId = 10, ForeignRecordingId = "rec-bonus" });
            GivenReleaseTracks(1, new Track { Id = 100, AlbumReleaseId = 1, ForeignRecordingId = "rec-other" });

            Subject.Import(_approvedDecisions, true);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.Is<TrackFile>(f => f.Id == 10), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_only_replace_files_of_the_imported_edition_when_several_are_monitored()
        {
            var editionFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 10).Build();
            var otherFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 11).Build();
            GivenSecondMonitoredEdition();
            GivenExistingFiles(editionFile, otherFile);

            Subject.Import(_approvedDecisions, true);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.Is<TrackFile>(f => f.Id == 10), DeleteMediaFileReason.Upgrade), Times.Once());
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.Is<TrackFile>(f => f.Id == 11), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_keep_other_editions_monitored_when_importing_one()
        {
            var extra = GivenSecondMonitoredEdition();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IReleaseService>()
                .Verify(v => v.SetMonitored(It.IsAny<AlbumRelease>()), Times.Never());
            _approvedDecisions.First().Item.Album.AlbumReleases.Value.Should().OnlyContain(r => r.Monitored);
            extra.Monitored.Should().BeTrue();
        }

        // Tomvis fork (MUSIC-64): importing wanted tracks of an album that wants only some must not recycle the rest
        [Test]
        public void should_not_replace_other_files_of_an_album_that_wants_only_some_tracks()
        {
            var editionFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 10).Build();
            var otherFile = Builder<TrackFile>.CreateNew().With(f => f.Id = 11).Build();
            var release = _approvedDecisions.First().Item.Release;
            release.Id = 1;
            release.Tracks = new List<Track> { new Track { Monitored = true }, new Track { Monitored = false } };
            GivenExistingFiles(editionFile, otherFile);

            Subject.Import(_approvedDecisions, true);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }
    }
}
