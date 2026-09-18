using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// SavepointCapabilities is more granular than SupportsSavepoints: T-SQL (SQL Server, Sybase) and
/// Oracle can create/rollback savepoints but have no explicit release statement at all, unlike the
/// fully ANSI-compliant dialects.
/// </summary>
public class SavepointCapabilitiesTests
{
    private static readonly SavepointCapabilities FullSupport =
        SavepointCapabilities.Create | SavepointCapabilities.Rollback | SavepointCapabilities.Release;

    private static readonly SavepointCapabilities CreateRollbackOnly =
        SavepointCapabilities.Create | SavepointCapabilities.Rollback;

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.Db2)]
    public void FullyAnsiCompliantDialects_SupportCreateRollbackAndRelease(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(FullSupport, dialect.SavepointCapabilities);
        Assert.Equal($"RELEASE SAVEPOINT {dialect.WrapObjectName("sp1")}", dialect.GetReleaseSavepointSql("sp1"));
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.SybaseASE)]
    [InlineData(SupportedDatabase.Oracle)]
    public void TSqlAndOracle_SupportCreateAndRollbackButNotRelease(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(CreateRollbackOnly, dialect.SavepointCapabilities);
        Assert.False(dialect.SavepointCapabilities.HasFlag(SavepointCapabilities.Release));
    }

    [Theory]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Snowflake)]
    public void UnsupportedDialects_HaveNoSavepointCapabilities(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(SavepointCapabilities.None, dialect.SavepointCapabilities);
        Assert.False(dialect.SupportsSavepoints);
    }

    private static ISqlDialect CreateDialect(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db.ToString());
        return db switch
        {
            SupportedDatabase.Sqlite => new SqliteDialect(factory, NullLogger.Instance),
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, NullLogger.Instance),
            SupportedDatabase.MySql => new MySqlDialect(factory, NullLogger.Instance),
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.TiDb => new TiDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.CockroachDb => new CockroachDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.YugabyteDb => new YugabyteDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.Firebird => new FirebirdDialect(factory, NullLogger.Instance),
            SupportedDatabase.Db2 => new Db2Dialect(factory, NullLogger.Instance),
            SupportedDatabase.SqlServer => new SqlServerDialect(factory, NullLogger.Instance),
            SupportedDatabase.SybaseASE => new SybaseDialect(factory, NullLogger.Instance),
            SupportedDatabase.Oracle => new OracleDialect(factory, NullLogger.Instance),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.Snowflake => new SnowflakeDialect(factory, NullLogger.Instance),
            _ => throw new System.NotSupportedException($"No dialect mapping for {db} in this test.")
        };
    }
}
