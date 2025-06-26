#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
}