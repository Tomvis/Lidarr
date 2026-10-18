using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Tomvis fork (MUSIC-135): an edition the bestedition pipeline swapped to is locked against upgrades
    [Migration(1002)]
    public class tomvis_release_locked : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AlbumReleases").AddColumn("Locked").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
