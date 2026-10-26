using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.TrackImport
{
    public interface IImportApprovedTracks
    {
        List<ImportResult> Import(List<ImportDecision<LocalTrack>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto);
    }

    public class ImportApprovedTracks : IImportApprovedTracks
    {
        private readonly IUpgradeMediaFiles _trackFileUpgrader;
        private readonly IMediaFileService _mediaFileService;
        private readonly IAudioTagService _audioTagService;
        private readonly ITrackService _trackService;
        private readonly IArtistService _artistService;
        private readonly IAddArtistService _addArtistService;
        private readonly IAlbumService _albumService;
        private readonly IRefreshAlbumService _refreshAlbumService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IExtraService _extraService;
        private readonly IDiskProvider _diskProvider;
        private readonly IHistoryService _historyService;
        private readonly IReleaseService _releaseService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ImportApprovedTracks(IUpgradeMediaFiles trackFileUpgrader,
                                    IMediaFileService mediaFileService,
                                    IAudioTagService audioTagService,
                                    ITrackService trackService,
                                    IArtistService artistService,
                                    IAddArtistService addArtistService,
                                    IAlbumService albumService,
                                    IRefreshAlbumService refreshAlbumService,
                                    IRootFolderService rootFolderService,
                                    IRecycleBinProvider recycleBinProvider,
                                    IExtraService extraService,
                                    IDiskProvider diskProvider,
                                    IHistoryService historyService,
                                    IReleaseService releaseService,
                                    IEventAggregator eventAggregator,
                                    IManageCommandQueue commandQueueManager,
                                    Logger logger)
        {
            _trackFileUpgrader = trackFileUpgrader;
            _mediaFileService = mediaFileService;
            _audioTagService = audioTagService;
            _trackService = trackService;
            _artistService = artistService;
            _addArtistService = addArtistService;
            _albumService = albumService;
            _refreshAlbumService = refreshAlbumService;
            _rootFolderService = rootFolderService;
            _recycleBinProvider = recycleBinProvider;
            _extraService = extraService;
            _diskProvider = diskProvider;
            _historyService = historyService;
            _releaseService = releaseService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public List<ImportResult> Import(List<ImportDecision<LocalTrack>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto)
        {
            var importResults = new List<ImportResult>();
            var allImportedTrackFiles = new List<TrackFile>();
            var allOldTrackFiles = new List<TrackFile>();
            var addedArtists = new List<Artist>();

            var albumDecisions = decisions.Where(e => e.Item.Album != null && e.Approved)
                .GroupBy(e => e.Item.Album.ForeignAlbumId).ToList();

            var iDecision = 1;
            foreach (var albumDecision in albumDecisions)
            {
                _logger.ProgressInfo($"Importing album {iDecision++}/{albumDecisions.Count}");

                var decisionList = albumDecision.ToList();

                var artist = EnsureArtistAdded(decisionList, addedArtists);

                if (artist == null)
                {
                    // failed to add the artist, carry on with next album
                    continue;
                }

                var album = EnsureAlbumAdded(decisionList);

                if (album == null)
                {
                    // failed to add the album, carry on with next one
                    continue;
                }

                var newRelease = albumDecision.First().Item.Release;

                // Tomvis fork (MUSIC-63): with several editions monitored, an import replaces only its own edition
                // and leaves the others monitored
                if (AlbumEditions.IsMultiEdition(album))
                {
                    if (replaceExisting)
                    {
                        RemoveExistingTrackFiles(artist, album, _mediaFileService.GetFilesByRelease(newRelease.Id));
                    }

                    _logger.Debug("Importing edition {0} [{1} tracks] of {2}", newRelease, newRelease.TrackCount, album);
                    _eventAggregator.PublishEvent(new AlbumEditedEvent(album, album));
                    continue;
                }

                // Tomvis fork (MUSIC-64): an album that wants only some tracks keeps its other files;
                // each imported track replaces only its own file (UpgradeTrackFile)
                // Tomvis fork (MUSIC-96): every other album recycles only the files the new release can't hold,
                // so a grab missing some tracks no longer loses them
                // Tomvis fork (MUSIC-177): without replaceExisting the release still switches below, so its files
                // move too; left on the old release's tracks, the imports collided with them at the same path
                if (!WantedTracks.IsPartial(newRelease))
                {
                    var cannotHold = FilesTheReleaseCannotHold(album, newRelease, decisionList, replaceExisting);

                    if (replaceExisting)
                    {
                        RemoveExistingTrackFiles(artist, album, cannotHold);
                    }
                }

                // set the correct release to be monitored before importing the new files
                _logger.Debug("Updating release to {0} [{1} tracks]", newRelease, newRelease.TrackCount);
                album.AlbumReleases = _releaseService.SetMonitored(newRelease);

                // Publish album edited event.
                // Deliberately don't put in the old album since we don't want to trigger an ArtistScan.
                _eventAggregator.PublishEvent(new AlbumEditedEvent(album, album));
            }

            var qualifiedImports = decisions
                .Where(decision => decision.Approved)
                .GroupBy(decision => decision.Item.Artist.Id)
                .SelectMany(group => group
                    .OrderByDescending(decision => decision.Item.Quality, new QualityModelComparer(group.First().Item.Artist.QualityProfile))
                    .ThenByDescending(decision => decision.Item.Size))
                .ToList();

            _logger.ProgressInfo($"Importing {qualifiedImports.Count} tracks");
            _logger.Debug($"Importing {qualifiedImports.Count} files. replaceExisting: {replaceExisting}");

            var filesToAdd = new List<TrackFile>(qualifiedImports.Count);
            var albumReleasesDict = new Dictionary<int, List<AlbumRelease>>(albumDecisions.Count);
            var trackImportedEvents = new List<TrackImportedEvent>(qualifiedImports.Count);

            foreach (var importDecision in qualifiedImports.OrderBy(e => e.Item.Tracks.Select(track => track.AbsoluteTrackNumber).MinOrDefault())
                                                           .ThenByDescending(e => e.Item.Size))
            {
                var localTrack = importDecision.Item;
                var oldFiles = new List<TrackFile>();

                try
                {
                    // check if already imported
                    if (importResults.SelectMany(r => r.ImportDecision.Item.Tracks)
                                         .Select(e => e.Id)
                                         .Intersect(localTrack.Tracks.Select(e => e.Id))
                                         .Any())
                    {
                        importResults.Add(new ImportResult(importDecision, "Track has already been imported"));
                        continue;
                    }

                    // cache album releases and set artist to speed up firing the TrackImported events
                    // (otherwise they'll be retrieved from the DB for each track)
                    if (!albumReleasesDict.ContainsKey(localTrack.Album.Id))
                    {
                        albumReleasesDict.Add(localTrack.Album.Id, localTrack.Album.AlbumReleases.Value);
                    }

                    if (!localTrack.Album.AlbumReleases.IsLoaded)
                    {
                        localTrack.Album.AlbumReleases = albumReleasesDict[localTrack.Album.Id];
                    }

                    localTrack.Album.Artist = localTrack.Artist;

                    foreach (var track in localTrack.Tracks)
                    {
                        track.Artist = localTrack.Artist;
                        track.AlbumRelease = localTrack.Release;
                        track.Album = localTrack.Album;
                    }

                    var trackFile = new TrackFile
                    {
                        Path = localTrack.Path.CleanFilePath(),
                        Size = localTrack.Size,
                        Modified = localTrack.Modified,
                        DateAdded = DateTime.UtcNow,
                        ReleaseGroup = localTrack.ReleaseGroup,
                        Quality = localTrack.Quality,
                        MediaInfo = localTrack.FileTrackInfo.MediaInfo,
                        AlbumId = localTrack.Album.Id,
                        Artist = localTrack.Artist,
                        Album = localTrack.Album,
                        Tracks = localTrack.Tracks
                    };

                    if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true)
                    {
                        var grabHistory = _historyService.FindByDownloadId(downloadClientItem.DownloadId)
                            .OrderByDescending(h => h.Date)
                            .FirstOrDefault(h => h.EventType == EntityHistoryEventType.Grabbed);

                        if (Enum.TryParse(grabHistory?.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                        {
                            trackFile.IndexerFlags = flags;
                        }
                    }
                    else
                    {
                        trackFile.IndexerFlags = localTrack.IndexerFlags;
                    }

                    bool copyOnly;
                    switch (importMode)
                    {
                        default:
                        case ImportMode.Auto:
                            copyOnly = downloadClientItem is { CanMoveFiles: false };
                            break;
                        case ImportMode.Move:
                            copyOnly = false;
                            break;
                        case ImportMode.Copy:
                            copyOnly = true;
                            break;
                    }

                    if (!localTrack.ExistingFile)
                    {
                        trackFile.SceneName = localTrack.SceneName;
                        trackFile.OriginalFilePath = GetOriginalFilePath(downloadClientItem, localTrack);

                        var moveResult = _trackFileUpgrader.UpgradeTrackFile(trackFile, localTrack, copyOnly);
                        oldFiles = moveResult.OldFiles;
                    }
                    else
                    {
                        // Reuse the DB row already mapped to this path. Deleting it here and inserting the
                        // replacement after the loop lost every row deleted so far whenever the import was
                        // interrupted (and the delete event recycled the file's extras).
                        var previousFile = _mediaFileService.GetFileWithPath(trackFile.Path);

                        if (previousFile != null)
                        {
                            if (filesToAdd.Any(f => f.Id == previousFile.Id))
                            {
                                importResults.Add(new ImportResult(importDecision, "Track has already been imported"));
                                continue;
                            }

                            trackFile.Id = previousFile.Id;

                            // A rescan builds the file from disk alone. Keep what the download told us,
                            // or custom formats score the bare filename and every search finds an "upgrade".
                            trackFile.SceneName = previousFile.SceneName;
                            trackFile.OriginalFilePath = previousFile.OriginalFilePath;

                            if (trackFile.ReleaseGroup.IsNullOrWhiteSpace())
                            {
                                trackFile.ReleaseGroup = previousFile.ReleaseGroup;
                            }

                            if (trackFile.IndexerFlags == 0)
                            {
                                trackFile.IndexerFlags = previousFile.IndexerFlags;
                            }
                        }

                        try
                        {
                            _audioTagService.WriteTags(trackFile, false);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.Warn(ex, "Failed to write tags for {0}: {1}. Try refreshing the artist to fix missing information.", trackFile.Path, ex.Message);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Unexpected error writing tags for existing track file {0}", trackFile.Path);
                        }
                    }

                    filesToAdd.Add(trackFile);
                    importResults.Add(new ImportResult(importDecision));

                    if (!localTrack.ExistingFile)
                    {
                        _extraService.ImportTrack(localTrack, trackFile, copyOnly);
                    }

                    allImportedTrackFiles.Add(trackFile);
                    allOldTrackFiles.AddRange(oldFiles);

                    // create all the import events here, but we can't publish until the trackfiles have been
                    // inserted and ids created
                    trackImportedEvents.Add(new TrackImportedEvent(localTrack, trackFile, oldFiles, !localTrack.ExistingFile, downloadClientItem));
                }
                catch (RootFolderNotFoundException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import track, Root folder missing."));
                }
                catch (DestinationAlreadyExistsException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    importResults.Add(new ImportResult(importDecision, "Failed to import track, Destination already exists."));
                }
                catch (UnauthorizedAccessException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import track, Permissions error"));
                }
                catch (RecycleBinException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import track, unable to move existing file to the Recycle Bin."));
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    importResults.Add(new ImportResult(importDecision, "Failed to import track"));
                }
            }

            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            var reusedFiles = filesToAdd.Where(f => f.Id > 0).ToList();
            _mediaFileService.AddMany(filesToAdd.Where(f => f.Id == 0).ToList());
            _mediaFileService.Update(reusedFiles);
            _logger.Debug($"Inserted new and updated {reusedFiles.Count} existing trackfiles in {watch.ElapsedMilliseconds}ms");
            filesToAdd.ForEach(f => f.Tracks.Value.ForEach(t => t.TrackFileId = f.Id));

            // tracks that pointed at a reused trackfile but no longer match it get unlinked
            var mappedTracks = filesToAdd.SelectMany(x => x.Tracks.Value).ToList();
            var mappedTrackIds = mappedTracks.Select(t => t.Id).ToHashSet();
            var unlinkedTracks = reusedFiles.Any()
                ? _trackService.GetTracksByFileId(reusedFiles.Select(f => f.Id)).Where(t => !mappedTrackIds.Contains(t.Id)).ToList()
                : new List<Track>();
            unlinkedTracks.ForEach(t => t.TrackFileId = 0);

            _trackService.SetFileIds(mappedTracks.Concat(unlinkedTracks).ToList());
            _logger.Debug($"TrackFileIds updated, total {watch.ElapsedMilliseconds}ms");

            // now that trackfiles have been inserted and ids generated, publish the import events
            foreach (var trackImportedEvent in trackImportedEvents)
            {
                _eventAggregator.PublishEvent(trackImportedEvent);
            }

            var albumImports = importResults.Where(e => e.ImportDecision.Item.Album != null)
                .GroupBy(e => e.ImportDecision.Item.Album.Id).ToList();

            foreach (var albumImport in albumImports)
            {
                var release = albumImport.First().ImportDecision.Item.Release;
                var album = _albumService.GetAlbum(albumImport.First().ImportDecision.Item.Album.Id);
                var artist = albumImport.First().ImportDecision.Item.Artist;

                if (albumImport.Where(e => e.Errors.Count == 0).ToList().Count > 0 && artist != null && album != null)
                {
                    // Tomvis fork (MUSIC-97): a file brought in from outside the library is a new download, as for
                    // TrackImportedEvent; upstream keyed this on replaceExisting, so a ManualImport without it
                    // sent no import notification
                    var newDownload = replaceExisting || albumImport.Any(e => e.Errors.Count == 0 && !e.ImportDecision.Item.ExistingFile);

                    _eventAggregator.PublishEvent(new AlbumImportedEvent(
                        artist,
                        album,
                        release,
                        allImportedTrackFiles.Where(s => s.AlbumId == album.Id).ToList(),
                        allOldTrackFiles.Where(s => s.AlbumId == album.Id).ToList(),
                        newDownload,
                        downloadClientItem));
                }
            }

            // Adding all the rejected decisions
            importResults.AddRange(decisions.Where(c => !c.Approved)
                                            .Select(d => new ImportResult(d, d.Rejections.Select(r => r.Reason).ToArray())));

            // Refresh any artists we added
            if (addedArtists.Any())
            {
                _commandQueueManager.Push(new BulkRefreshArtistCommand(addedArtists.Select(x => x.Id).ToList(), true));
            }

            return importResults;
        }

        private Artist EnsureArtistAdded(List<ImportDecision<LocalTrack>> decisions, List<Artist> addedArtists)
        {
            var artist = decisions.First().Item.Artist;

            if (artist.Id == 0)
            {
                var dbArtist = _artistService.FindById(artist.ForeignArtistId);

                if (dbArtist == null)
                {
                    _logger.Debug($"Adding remote artist {artist}");
                    var rootFolder = _rootFolderService.GetBestRootFolder(decisions.First().Item.Path);

                    artist.RootFolderPath = rootFolder.Path;
                    artist.MetadataProfileId = rootFolder.DefaultMetadataProfileId;
                    artist.QualityProfileId = rootFolder.DefaultQualityProfileId;
                    artist.Monitored = rootFolder.DefaultMonitorOption != MonitorTypes.None;
                    artist.MonitorNewItems = rootFolder.DefaultNewItemMonitorOption;
                    artist.Tags = rootFolder.DefaultTags;
                    artist.AddOptions = new AddArtistOptions
                    {
                        SearchForMissingAlbums = false,
                        Monitored = artist.Monitored,
                        Monitor = rootFolder.DefaultMonitorOption
                    };

                    try
                    {
                        dbArtist = _addArtistService.AddArtist(artist, false);
                        addedArtists.Add(dbArtist);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to add artist {0}", artist);
                        foreach (var decision in decisions)
                        {
                            decision.Reject(new Rejection("Failed to add missing artist", RejectionType.Temporary));
                        }

                        return null;
                    }
                }

                // Put in the newly loaded artist
                foreach (var decision in decisions)
                {
                    decision.Item.Artist = dbArtist;
                    decision.Item.Album.Artist = dbArtist;
                    decision.Item.Album.ArtistMetadataId = dbArtist.ArtistMetadataId;
                }

                artist = dbArtist;
            }

            return artist;
        }

        private Album EnsureAlbumAdded(List<ImportDecision<LocalTrack>> decisions)
        {
            var album = decisions.First().Item.Album;

            if (album.Id == 0)
            {
                var dbAlbum = _albumService.FindById(album.ForeignAlbumId);

                if (dbAlbum == null)
                {
                    _logger.Debug($"Adding remote album {album}");
                    try
                    {
                        album.Monitored = album.Artist.Value.Monitored;
                        album.Added = DateTime.UtcNow;
                        _albumService.InsertMany(new List<Album> { album });
                        _refreshAlbumService.RefreshAlbumInfo(album, new List<Album> { album }, false);
                        dbAlbum = _albumService.FindById(album.ForeignAlbumId);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to add album {0}", album);
                        RejectAlbum(decisions);

                        return null;
                    }
                }

                var release = dbAlbum.AlbumReleases.Value.ExclusiveOrDefault(x => x.ForeignReleaseId == decisions.First().Item.Release.ForeignReleaseId);
                if (release == null)
                {
                    RejectAlbum(decisions);
                    return null;
                }

                // Populate the new DB album
                foreach (var decision in decisions)
                {
                    decision.Item.Album = dbAlbum;
                    decision.Item.Release = release;
                    var trackIds = decision.Item.Tracks.Select(x => x.ForeignTrackId).ToList();
                    decision.Item.Tracks = release.Tracks.Value.Where(x => trackIds.Contains(x.ForeignTrackId)).ToList();
                }
            }

            return album;
        }

        private void RejectAlbum(List<ImportDecision<LocalTrack>> decisions)
        {
            foreach (var decision in decisions)
            {
                decision.Reject(new Rejection("Failed to add missing album", RejectionType.Temporary));
            }
        }

        // Tomvis fork (MUSIC-96): a file on a track of the imported release stays (UpgradeTrackFile replaces it if the
        // import brings that track). After a release switch, a file whose recording the new release has but the import
        // doesn't bring moves to that track. The rest (recordings the import brings, the new release lacks, or no track
        // at all) are recycled as upstream did.
        private List<TrackFile> FilesTheReleaseCannotHold(Album album, AlbumRelease newRelease, List<ImportDecision<LocalTrack>> decisions, bool replaceExisting)
        {
            var files = _mediaFileService.GetFilesByAlbum(album.Id);

            if (!files.Any())
            {
                return files;
            }

            // With replaceExisting a file whose recording the import brings is recycled up front; without it the
            // file moves to the imported track, where UpgradeTrackFile replaces it
            var importedTrackIds = decisions.SelectMany(d => d.Item.Tracks).Select(t => t.Id).ToHashSet();
            var freeTracks = (_trackService.GetTracksByRelease(newRelease.Id) ?? new List<Track>())
                .Where(t => !t.HasFile && !(replaceExisting && importedTrackIds.Contains(t.Id)))
                .ToList();
            var tracksByFile = (_trackService.GetTracksByFileId(files.Select(f => f.Id)) ?? new List<Track>())
                .ToLookup(t => t.TrackFileId);

            var relinked = new List<Track>();
            var recycle = new List<TrackFile>();

            foreach (var file in files)
            {
                var fileTracks = tracksByFile[file.Id].ToList();

                if (fileTracks.Any(t => t.AlbumReleaseId == newRelease.Id))
                {
                    continue;
                }

                var recordings = fileTracks.Select(t => t.ForeignRecordingId).ToHashSet();
                var targets = freeTracks.Where(t => recordings.Contains(t.ForeignRecordingId)).ToList();

                if (!targets.Any())
                {
                    recycle.Add(file);
                    continue;
                }

                _logger.Debug("Moving {0} to {1} track(s) of {2}", file, targets.Count, newRelease);
                fileTracks.ForEach(t => t.TrackFileId = 0);
                targets.ForEach(t => t.TrackFileId = file.Id);
                freeTracks.RemoveAll(targets.Contains);
                relinked.AddRange(fileTracks.Concat(targets));
            }

            if (relinked.Any())
            {
                _trackService.SetFileIds(relinked);
            }

            // UpgradeTrackFile reads the file off the decision's own track instances, not the ones relinked above
            var movedTo = relinked.Where(t => t.TrackFileId > 0 && importedTrackIds.Contains(t.Id))
                .ToDictionary(t => t.Id, t => files.Single(f => f.Id == t.TrackFileId));

            foreach (var track in decisions.SelectMany(d => d.Item.Tracks).Where(t => movedTo.ContainsKey(t.Id)))
            {
                track.TrackFileId = movedTo[track.Id].Id;
                track.TrackFile = movedTo[track.Id];
            }

            return recycle;
        }

        private void RemoveExistingTrackFiles(Artist artist, Album album, List<TrackFile> previousFiles)
        {
            var rootFolder = _diskProvider.GetParentFolder(artist.Path);

            _logger.Debug($"Deleting {previousFiles.Count} existing files for {album}");

            foreach (var previousFile in previousFiles)
            {
                var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(previousFile.Path));
                if (_diskProvider.FileExists(previousFile.Path))
                {
                    _logger.Debug("Removing existing track file: {0}", previousFile);
                    _recycleBinProvider.DeleteFile(previousFile.Path, subfolder);
                }

                _mediaFileService.Delete(previousFile, DeleteMediaFileReason.Upgrade);
            }
        }

        private string GetOriginalFilePath(DownloadClientItem downloadClientItem, LocalTrack localTrack)
        {
            var path = localTrack.Path;

            if (downloadClientItem != null && !downloadClientItem.OutputPath.IsEmpty)
            {
                var outputDirectory = downloadClientItem.OutputPath.Directory.ToString();

                if (outputDirectory.IsParentPath(path))
                {
                    return outputDirectory.GetRelativePath(path);
                }
            }

            var folderEpisodeInfo = localTrack.FolderAlbumInfo;

            if (folderEpisodeInfo != null)
            {
                var folderPath = path.GetAncestorPath(folderEpisodeInfo.ReleaseTitle);

                if (folderPath != null)
                {
                    return folderPath.GetParentPath().GetRelativePath(path);
                }
            }

            var parentPath = path.GetParentPath();
            var grandparentPath = parentPath.GetParentPath();

            if (grandparentPath != null)
            {
                return grandparentPath.GetRelativePath(path);
            }

            return Path.GetFileName(path);
        }
    }
}
