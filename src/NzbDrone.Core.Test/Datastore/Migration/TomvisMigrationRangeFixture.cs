using System.Linq;
using System.Reflection;
using FluentAssertions;
using FluentMigrator;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Tomvis fork (MUSIC-62): fork migrations live in 1000+ as tomvis_* so upstream's numbering never collides.
    [TestFixture]
    public class TomvisMigrationRangeFixture : TestBase
    {
        public const long ForkRangeStart = 1000;

        private static (string Name, long Version)[] Migrations()
        {
            return typeof(NzbDroneMigrationBase).Assembly.GetTypes()
                .Select(t => (Type: t, Attr: t.GetCustomAttribute<MigrationAttribute>()))
                .Where(x => x.Attr != null)
                .Select(x => (x.Type.Name, x.Attr.Version))
                .ToArray();
        }

        [Test]
        public void fork_migrations_should_use_the_fork_range()
        {
            Migrations().Where(m => m.Name.StartsWith("tomvis_") && m.Version < ForkRangeStart)
                .Should().BeEmpty();
        }

        [Test]
        public void fork_range_should_hold_only_fork_migrations()
        {
            Migrations().Where(m => m.Version >= ForkRangeStart && !m.Name.StartsWith("tomvis_"))
                .Should().BeEmpty();
        }

        [Test]
        public void migrations_should_be_in_the_namespace_the_runner_scans()
        {
            // MigrationController filters on this exact namespace (no nested namespaces)
            typeof(NzbDroneMigrationBase).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<MigrationAttribute>() != null && t.Namespace != "NzbDrone.Core.Datastore.Migration")
                .Should().BeEmpty();
        }
    }
}
