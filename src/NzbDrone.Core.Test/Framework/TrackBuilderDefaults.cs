using FizzWare.NBuilder;
using NUnit.Framework;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Test
{
    // Tomvis fork (MUSIC-64): built tracks keep the production default (wanted) instead of NBuilder's alternating bools
    [SetUpFixture]
    public class TrackBuilderDefaults
    {
        [OneTimeSetUp]
        public void KeepTracksWanted()
        {
            BuilderSetup.DisablePropertyNamingFor<Track, bool>(t => t.Monitored);
        }
    }
}
