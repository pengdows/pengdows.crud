#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public static class DataSourceTestData
{
    public static IEnumerable<object[]> AllDatabases()
    {
        foreach (SupportedDatabase db in Enum.GetValues(typeof(SupportedDatabase)))
        {
            if (db == SupportedDatabase.Unknown) continue;

            var schema = DataSourceInformation.BuildEmptySchema(
                db.ToString(),
                "1.2.3",
                db == SupportedDatabase.Sqlite ? "@p[0-9]+" : "@[0-9]+",
                "@{0}",
                64,
                @"@\\w+",
                @"[@:]\w+",
                db != SupportedDatabase.Sqlite
            );

            var versionSql = db switch
            {
                SupportedDatabase.SqlServer => "SELECT @@VERSION",
                SupportedDatabase.MySql => "SELECT VERSION()",
                SupportedDatabase.MariaDb => "SELECT VERSION()",
                SupportedDatabase.PostgreSql => "SELECT version()",
                SupportedDatabase.CockroachDb => "SELECT version()",
                SupportedDatabase.Sqlite => "SELECT sqlite_version()",
                SupportedDatabase.Firebird => "SELECT rdb$get_context('SYSTEM', 'VERSION')",
                SupportedDatabase.Oracle => "SELECT * FROM v$version",
                _ => string.Empty
            };

            var scalars = new Dictionary<string, object>
            {
                [versionSql] = $"{db} v1.2.3"
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
        var info = DataSourceInformation.Create(conn, NullLoggerFactory.Instance);

        // Assert: product detection
        //Assert.Equal(db, info.Product);

        // Assert: version parsing
        //Assert.Contains("v1.2.3", info.DatabaseProductVersion);

        // Assert: parameter marker
        var expectedMarker = db switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.Oracle => ":",
            _ => "@"
        };
        Assert.Equal(expectedMarker, info.ParameterMarker);

        // Assert: merge support
        var canMerge = db == SupportedDatabase.SqlServer
                       || db == SupportedDatabase.Oracle
                       || db == SupportedDatabase.Firebird;
        Assert.Equal(canMerge, info.SupportsMerge);

        // Assert: insert-on-conflict support
        var canConflict = (new[]
        {
            SupportedDatabase.PostgreSql,
            SupportedDatabase.CockroachDb,
            SupportedDatabase.Sqlite,
            SupportedDatabase.MySql,
            SupportedDatabase.MariaDb
        }).Contains(db);
        Assert.Equal(canConflict, info.SupportsInsertOnConflict);

        // Assert: proc wrap style
        var expectedWrap = db switch
        {
            SupportedDatabase.SqlServer => ProcWrappingStyle.Exec,
            SupportedDatabase.Oracle => ProcWrappingStyle.Oracle,
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => ProcWrappingStyle.Call,
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => ProcWrappingStyle.PostgreSQL,
            SupportedDatabase.Firebird => ProcWrappingStyle.ExecuteProcedure,
            _ => ProcWrappingStyle.None
        };
        var expectedRequiresStoredProcParameterNameMatch = db switch
        {
            SupportedDatabase.Firebird or SupportedDatabase.Sqlite or SupportedDatabase.SqlServer
                or SupportedDatabase.MySql or SupportedDatabase.MariaDb => false,
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

        var info = DataSourceInformation.Create(tracked, NullLoggerFactory.Instance);

        var result = info.GetDatabaseVersion(tracked);
        Assert.Equal("42", result);
    }

    [Fact]
    public void GetDatabaseVersion_UnknownProduct_ReturnsUnknown()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var connection = factory.CreateConnection();
        connection.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var tracked = new FakeTrackedConnection(connection, DataSourceInformation.BuildEmptySchema("test", "1", "@", "@{0}", 64, "@w+", "@w+", true), new Dictionary<string, object>());
        var info = DataSourceInformation.Create(tracked, NullLoggerFactory.Instance);

        var prop = typeof(DataSourceInformation).GetProperty("Product", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        prop!.SetValue(info, SupportedDatabase.Unknown);

        var result = info.GetDatabaseVersion(tracked);
        Assert.Equal("Unknown Database Version", result);
    }
    private class SqliteVersionCommand : FakeDbCommand
    {
        public SqliteVersionCommand(DbConnection connection) : base(connection) { }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            if (CommandText == "SELECT sqlite_version()")
            {
                return new FakeDbDataReader(new[] { new Dictionary<string, object>{{"v","3.0"}} });
            }
            return base.ExecuteDbDataReader(behavior);
        }
    }

    private class SqliteVersionConnection : FakeDbConnection
    {
        protected override DbCommand CreateDbCommand()
        {
            return new SqliteVersionCommand(this);
        }
    }

    [Fact]
    public void GetSchema_UsesEmbeddedForSqlite()
    {
        var conn = new SqliteVersionConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        using var tracked = new TrackedConnection(conn);
        var info = DataSourceInformation.Create(tracked, NullLoggerFactory.Instance);

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
        var info = DataSourceInformation.Create(tracked, NullLoggerFactory.Instance);

        var schema = info.GetSchema(tracked);
        Assert.Contains("SQL Server", schema.Rows[0].Field<string>("DataSourceProductName"));
        Assert.Equal("{0}", schema.Rows[0].Field<string>("ParameterMarkerFormat"));
    }

    [Theory]
    [InlineData("SQL Server 2019", SupportedDatabase.SqlServer)]
    [InlineData("MariaDB 10.3", SupportedDatabase.MariaDb)]
    [InlineData("MySQL 8.0", SupportedDatabase.MySql)]
    [InlineData("Npgsql", SupportedDatabase.PostgreSql)]
    [InlineData("PostgreSQL 14", SupportedDatabase.PostgreSql)]
    [InlineData("Oracle Database", SupportedDatabase.Oracle)]
    [InlineData("SQLite", SupportedDatabase.Sqlite)]
    [InlineData("Firebird", SupportedDatabase.Firebird)]
    [InlineData("Something Else", SupportedDatabase.Unknown)]
    [InlineData(null, SupportedDatabase.Unknown)]
    public void InferDatabaseProduct_ReturnsExpected(string name, SupportedDatabase expected)
    {
        var method = typeof(DataSourceInformation).GetMethod(
            "InferDatabaseProduct",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (SupportedDatabase)method.Invoke(null, new object?[] { name })!;
        Assert.Equal(expected, result);
    }
}
