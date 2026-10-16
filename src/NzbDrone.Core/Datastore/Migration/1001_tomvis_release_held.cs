using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Tomvis fork (MUSIC-102): a monitored extra edition can be held (a candidate awaiting a verdict)
    [Migration(1001)]
    public class tomvis_release_held : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AlbumReleases").AddColumn("Held").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
