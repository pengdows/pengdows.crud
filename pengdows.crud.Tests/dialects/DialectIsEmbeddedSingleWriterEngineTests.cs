#region

using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// ISqlDialect.IsEmbeddedSingleWriterEngine identifies SQLite/DuckDB-like engines specifically —
/// narrower than !IsClientServerDatabase, which is also false for the Unknown-database fallback.
/// DatabaseContext.WarnOnModeMismatch's file-based-lock-contention warning is SQLite/DuckDB-specific
/// wording ("SQLITE_BUSY"), so it must not fire for Unknown; that's why this needs its own property
/// rather than reusing IsClientServerDatabase's broader "not client-server" bucket.
/// </summary>
public class DialectIsEmbeddedSingleWriterEngineTests
{
    private static ISqlDialect CreateDialect(SupportedDatabase db) =>
        SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger.Instance);

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void EmbeddedEngines_ReportTrue(SupportedDatabase db)
    {
        Assert.True(CreateDialect(db).IsEmbeddedSingleWriterEngine);
    }

    public static IEnumerable<object[]> NonEmbeddedDatabases()
    {
        yield return new object[] { SupportedDatabase.Unknown };
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.Firebird };
        yield return new object[] { SupportedDatabase.Db2 };
    }

    [Theory]
    [MemberData(nameof(NonEmbeddedDatabases))]
    public void EverythingElse_IncludingUnknown_ReportsFalse(SupportedDatabase db)
    {
        Assert.False(CreateDialect(db).IsEmbeddedSingleWriterEngine);
    }
}
