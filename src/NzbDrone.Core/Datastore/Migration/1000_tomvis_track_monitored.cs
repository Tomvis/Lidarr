using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Tomvis fork (MUSIC-64): per-track wanted flag; existing tracks follow their album (all wanted)
    [Migration(1000)]
    public class tomvis_track_monitored : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Tracks").AddColumn("Monitored").AsBoolean().NotNullable().WithDefaultValue(true);
        }
    }
}
