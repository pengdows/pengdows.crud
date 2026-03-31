using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Verifies that each dialect self-reports its constraint and isolation-level capabilities
/// so that integration-test gates derive from the dialect instead of hardcoded provider lists.
/// </summary>
public class DialectCapabilityTests
{
    private static ISqlDialect Create(SupportedDatabase db) =>
        SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger<SqlDialect>.Instance);

    // =========================================================================
    // ReadCommittedCompatibleIsolationLevel
    // CockroachDB and DuckDB only support Serializable; everyone else ReadCommitted.
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void ReadCommittedCompatibleIsolationLevel_IsSerializable(SupportedDatabase db)
    {
        Assert.Equal(IsolationLevel.Serializable, Create(db).ReadCommittedCompatibleIsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.Snowflake)]
    public void ReadCommittedCompatibleIsolationLevel_IsReadCommitted(SupportedDatabase db)
    {
        Assert.Equal(IsolationLevel.ReadCommitted, Create(db).ReadCommittedCompatibleIsolationLevel);
    }

    // =========================================================================
    // EnforcesConstraints
    // Snowflake does not enforce any constraints.
    // =========================================================================

    [Fact]
    public void EnforcesConstraints_IsFalse_ForSnowflake()
    {
        Assert.False(Create(SupportedDatabase.Snowflake).EnforcesConstraints);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void EnforcesConstraints_IsTrue(SupportedDatabase db)
    {
        Assert.True(Create(db).EnforcesConstraints);
    }

    // =========================================================================
    // EnforcesForeignKeyConstraints
    // SQLite and TiDB do not enforce FK constraints by default; Snowflake enforces nothing.
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.Snowflake)]
    public void EnforcesForeignKeyConstraints_IsFalse(SupportedDatabase db)
    {
        Assert.False(Create(db).EnforcesForeignKeyConstraints);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void EnforcesForeignKeyConstraints_IsTrue(SupportedDatabase db)
    {
        Assert.True(Create(db).EnforcesForeignKeyConstraints);
    }

    // =========================================================================
    // SupportsUniqueConstraints
    // SQLite, DuckDB, and Snowflake excluded (test infra limitation / no enforcement).
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Snowflake)]
    public void SupportsUniqueConstraints_IsFalse(SupportedDatabase db)
    {
        Assert.False(Create(db).SupportsUniqueConstraints);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.TiDb)]
    public void SupportsUniqueConstraints_IsTrue(SupportedDatabase db)
    {
        Assert.True(Create(db).SupportsUniqueConstraints);
    }

    // =========================================================================
    // SupportsCheckConstraints
    // TiDB parses CHECK DDL but does not enforce; SQLite, DuckDB, Snowflake excluded.
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Snowflake)]
    [InlineData(SupportedDatabase.TiDb)]
    public void SupportsCheckConstraints_IsFalse(SupportedDatabase db)
    {
        Assert.False(Create(db).SupportsCheckConstraints);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void SupportsCheckConstraints_IsTrue(SupportedDatabase db)
    {
        Assert.True(Create(db).SupportsCheckConstraints);
    }

    // =========================================================================
    // BooleanDbType
    // SQLite has no native BOOLEAN column — bind as INTEGER (Int32).
    // =========================================================================

    [Fact]
    public void BooleanDbType_IsSqliteInt32()
    {
        Assert.Equal(DbType.Int32, Create(SupportedDatabase.Sqlite).BooleanDbType);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Snowflake)]
    public void BooleanDbType_IsDbTypeBoolean(SupportedDatabase db)
    {
        Assert.Equal(DbType.Boolean, Create(db).BooleanDbType);
    }
}
