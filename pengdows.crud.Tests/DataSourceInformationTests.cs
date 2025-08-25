#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.wrappers;
using pengdows.crud.dialects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public static class DataSourceTestData
{
    public static IEnumerable<object[]> AllDatabases()
    {
        foreach (SupportedDatabase db in Enum.GetValues(typeof(SupportedDatabase)))
        {
            if (db == SupportedDatabase.Unknown)
            {
                continue;
            }

            var productName = db switch
            {
                SupportedDatabase.SqlServer => "SQL Server",
                SupportedDatabase.MySql => "MySQL",
                SupportedDatabase.MariaDb => "MariaDB",
                SupportedDatabase.PostgreSql => "PostgreSQL",
                SupportedDatabase.CockroachDb => "CockroachDB",
                SupportedDatabase.Sqlite => "SQLite",
                SupportedDatabase.Firebird => "Firebird",
                SupportedDatabase.Oracle => "Oracle Database",
                _ => db.ToString()
            };

            var markerFormat = db switch
            {
                SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.Oracle => ":{0}",
                SupportedDatabase.DuckDB => "$" + "{0}",
                _ => "@{0}"
            };

            var schema = DataSourceInformation.BuildEmptySchema(
                productName,
                "1.2.3",
                db == SupportedDatabase.Sqlite ? "@p[0-9]+" : "@[0-9]+",
                markerFormat,
                64,
                @"@\\w+",
                @"[@:]\w+",
                db != SupportedDatabase.Sqlite
            );

            var factory = new FakeDbFactory(db.ToString());

            SqlDialect dialect = db switch
            {
                SupportedDatabase.SqlServer => new SqlServerDialect(factory, NullLogger.Instance),
                SupportedDatabase.MySql => new MySqlDialect(factory, NullLogger.Instance),
                SupportedDatabase.MariaDb => new MySqlDialect(factory, NullLogger.Instance),
                SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, NullLogger.Instance),
                SupportedDatabase.CockroachDb => new PostgreSqlDialect(factory, NullLogger.Instance),
                SupportedDatabase.Sqlite => new SqliteDialect(factory, NullLogger.Instance),
                SupportedDatabase.Firebird => new FirebirdDialect(factory, NullLogger.Instance),
                SupportedDatabase.Oracle => new OracleDialect(factory, NullLogger.Instance),
                SupportedDatabase.DuckDB => new DuckDbDialect(factory, NullLogger.Instance),
                _ => new Sql92Dialect(factory, NullLogger.Instance)
            };

            var versionSql = dialect.GetVersionQuery();

            var versionString = db switch
            {
                SupportedDatabase.PostgreSql => "PostgreSQL 15.0",
                _ => $"{db} v1.2.3"
            };

            var scalars = new Dictionary<string, object>
            {
                [versionSql] = versionString
            };

            yield return new object[] { db, schema, scalars };
        }
    }
}

public class DataSourceInformationTests
{
    [Theory]
    [MemberData(nameof(DataSourceTestData.AllDatabases), MemberType = typeof(DataSourceTestData))]
    public void DataSourceInformation_Should_Configure_Each_Database(
        SupportedDatabase db,
        DataTable schema,
        Dictionary<string, object> scalars)
    {
        var factory = new FakeDbFactory(db.ToString());
        // Arrange
        var x = factory.CreateConnection();
        x.ConnectionString = $"Data Source=test;Data Source=test;EmulatedProduct={db}";
        var conn = new FakeTrackedConnection(x, schema, scalars);

        // Act
        var info = DataSourceInformation.Create(conn, factory, NullLoggerFactory.Instance);

        // Assert: product detection
        //Assert.Equal(db, info.Product);

        // Assert: version parsing
        //Assert.Contains("v1.2.3", info.DatabaseProductVersion);

        // Assert: parameter marker
        var expectedMarker = db switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.Oracle => ":",
            SupportedDatabase.DuckDB => "$",
            _ => "@"
        };
        Assert.Equal(expectedMarker, info.ParameterMarker);

        // Assert: major version parsing
        var expectedMajor = db == SupportedDatabase.PostgreSql ? 15 : 1;
        Assert.Equal(expectedMajor, info.ParsedVersion?.Major);

        // Assert: merge support
        var canMerge = db == SupportedDatabase.SqlServer
                       || db == SupportedDatabase.Oracle
                       || (db == SupportedDatabase.Firebird && info.ParsedVersion?.Major >= 2)
                       || (db == SupportedDatabase.PostgreSql && info.ParsedVersion?.Major > 14);
        Assert.Equal(canMerge, info.SupportsMerge);

        // Assert: insert-on-conflict support
        var canConflict = (new[]
        {
            SupportedDatabase.PostgreSql,
            SupportedDatabase.CockroachDb,
            SupportedDatabase.Sqlite,
            SupportedDatabase.DuckDB
        }).Contains(db);
        Assert.Equal(canConflict, info.SupportsInsertOnConflict);

        var canOnDuplicateKey = (new[]
        {
            SupportedDatabase.MySql,
            SupportedDatabase.MariaDb
        }).Contains(db);
        Assert.Equal(canOnDuplicateKey, info.SupportsOnDuplicateKey);

        // Assert: proc wrap style
        var expectedWrap = db switch
        {
            SupportedDatabase.SqlServer => ProcWrappingStyle.Exec,
            SupportedDatabase.Oracle => ProcWrappingStyle.Oracle,
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => ProcWrappingStyle.Call,
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => ProcWrappingStyle.PostgreSQL,
            SupportedDatabase.Firebird => ProcWrappingStyle.Call,
            _ => ProcWrappingStyle.None
        };
        var expectedRequiresStoredProcParameterNameMatch = db switch
        {
            SupportedDatabase.Firebird or SupportedDatabase.Sqlite or SupportedDatabase.SqlServer
                or SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.DuckDB => false,
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.Oracle => true,
            _ => true
        };

        Assert.Equal(expectedWrap, info.ProcWrappingStyle);

        // Assert: named parameters flags
        Assert.Equal(db != SupportedDatabase.Unknown, info.SupportsNamedParameters);
        Assert.Equal(expectedRequiresStoredProcParameterNameMatch, info.RequiresStoredProcParameterNameMatch);
    }

    [Theory]
    [MemberData(nameof(DataSourceTestData.AllDatabases), MemberType = typeof(DataSourceTestData))]
    public void GetDatabaseVersion_Returns_Version(SupportedDatabase db, DataTable schema, Dictionary<string, object> scalars)
    {
        var factory = new FakeDbFactory(db.ToString());
        var connection = factory.CreateConnection();
        connection.ConnectionString = $"Data Source=test;EmulatedProduct={db}";
        var tracked = new FakeTrackedConnection(connection, schema, scalars);

        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        var info = new DataSourceInformation(dialect);

        var result = dialect.GetDatabaseVersion(tracked);
        var expected = scalars.Values.First().ToString();
        Assert.Equal(expected, result);
    }

    private static ITrackedConnection BuildSqliteConnectionMock()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var conn = (FakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";

        var row = new Dictionary<string, object> { { "version", "3.0" } };
        // One result for IsSqliteAsync, one for GetVersionAsync, and one for IsSqliteSync
        conn.EnqueueReaderResult(new[] { row });
        conn.EnqueueReaderResult(new[] { row });
        conn.EnqueueReaderResult(new[] { row });

        conn.Open();
        return new TrackedConnection(conn);
    }

    [Fact]
    public void GetSchema_UsesEmbeddedForSqlite()
    {
        var tracked = BuildSqliteConnectionMock();
        var info = DataSourceInformation.Create(tracked, new FakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);

        var schema = info.GetSchema(tracked);
        Assert.Equal("SQLite", schema.Rows[0].Field<string>("DataSourceProductName"));
        Assert.Equal("@{0}", schema.Rows[0].Field<string>("ParameterMarkerFormat"));
    }

    [Fact]
    public void GetSchema_NonSqlite_UsesConnectionSchema()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        using var tracked = new TrackedConnection(conn);
        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);

        var schema = info.GetSchema(tracked);
        Assert.Contains("SQL Server", schema.Rows[0].Field<string>("DataSourceProductName"));
        Assert.Equal("{0}", schema.Rows[0].Field<string>("ParameterMarkerFormat"));
    }

}
