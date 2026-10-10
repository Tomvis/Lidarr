using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshTrackServiceFixture : CoreTest<RefreshTrackService>
    {
        private AlbumRelease _release;
        private List<Track> _allTracks;

        [SetUp]
        public void Setup()
        {
            _release = Builder<AlbumRelease>.CreateNew().Build();
            _allTracks = Builder<Track>.CreateListOfSize(20)
                .All()
                .BuildList();
        }

        [Test]
        public void updated_track_should_not_have_null_album_release()
        {
            var add = new List<Track>();
            var update = new List<Track>();
            var merge = new List<Tuple<Track, Track>>();
            var delete = new List<Track>();
            var upToDate = new List<Track>();

            upToDate.AddRange(_allTracks.Take(10));

            var toUpdate = _allTracks[10].JsonClone();
            toUpdate.Title = "title to update";
            toUpdate.AlbumRelease = _release;

            update.Add(toUpdate);

            Subject.RefreshTrackInfo(add, update, merge, delete, upToDate, _allTracks, false);

            Mocker.GetMock<IAudioTagService>()
                .Verify(v => v.SyncTags(It.Is<List<Track>>(x => x.Count == 1 &&
                                                           x[0].AlbumRelease != null &&
                                                           x[0].AlbumRelease.IsLoaded == true)));
        }

        // Tomvis fork (MUSIC-64): the wanted flag survives MusicBrainz track merges
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void new_merge_target_should_take_wanted_flag_of_merged_track(bool mergedWanted, bool expected)
        {
            var toMerge = _allTracks[0];
            toMerge.Monitored = mergedWanted;
            var target = _allTracks[1];
            target.Monitored = true;

            var add = new List<Track> { target };
            var merge = new List<Tuple<Track, Track>> { Tuple.Create(toMerge, target) };

            Subject.RefreshTrackInfo(add, new List<Track>(), merge, new List<Track>(), new List<Track>(), _allTracks, false);

            target.Monitored.Should().Be(expected);
        }

        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        public void existing_merge_target_should_stay_wanted_if_either_track_was_wanted(bool targetWanted, bool mergedWanted, bool expected)
        {
            var toMerge = _allTracks[0];
            toMerge.Monitored = mergedWanted;
            var target = _allTracks[1];
            target.Monitored = targetWanted;

            var merge = new List<Tuple<Track, Track>> { Tuple.Create(toMerge, target) };

            Subject.RefreshTrackInfo(new List<Track>(), new List<Track>(), merge, new List<Track>(), new List<Track>(), _allTracks, false);

            target.Monitored.Should().Be(expected);
        }
    }
}
