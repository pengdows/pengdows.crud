#region

using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// ISqlDialect.IsClientServerDatabase is the single source of truth other layers (WarnOnModeMismatch,
/// CoerceMode) should consult instead of maintaining their own SupportedDatabase switch. Covering every
/// dialect once here means a new database only needs to get its dialect's default right, not a second
/// switch elsewhere — see CLAUDE.md's "Adding a New Database" checklist.
/// </summary>
public class DialectIsClientServerDatabaseTests
{
    public static IEnumerable<object[]> ClientServerDatabases()
    {
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.CockroachDb };
        yield return new object[] { SupportedDatabase.YugabyteDb };
        yield return new object[] { SupportedDatabase.TiDb };
        yield return new object[] { SupportedDatabase.MySql };
        yield return new object[] { SupportedDatabase.AuroraMySql };
        yield return new object[] { SupportedDatabase.MariaDb };
        yield return new object[] { SupportedDatabase.Oracle };
        yield return new object[] { SupportedDatabase.Firebird };
        yield return new object[] { SupportedDatabase.Snowflake };
        yield return new object[] { SupportedDatabase.AuroraPostgreSql };
        yield return new object[] { SupportedDatabase.Db2 };
    }

    public static IEnumerable<object[]> EmbeddedOrUnknownDatabases()
    {
        yield return new object[] { SupportedDatabase.Sqlite };
        yield return new object[] { SupportedDatabase.DuckDB };
        yield return new object[] { SupportedDatabase.Unknown };
    }

    [Theory]
    [MemberData(nameof(ClientServerDatabases))]
    public void ClientServerDialects_ReportIsClientServerDatabase_True(SupportedDatabase db)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger.Instance);
        Assert.True(dialect.IsClientServerDatabase);
    }

    [Theory]
    [MemberData(nameof(EmbeddedOrUnknownDatabases))]
    public void EmbeddedOrUnknownDialects_ReportIsClientServerDatabase_False(SupportedDatabase db)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger.Instance);
        Assert.False(dialect.IsClientServerDatabase);
    }
}
