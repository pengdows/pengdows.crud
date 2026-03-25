using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that BuildWrappedTableName respects SupportsNamespaces — dialects that do not
/// support schemas must omit the schema prefix even when [Table("Name","Schema")] is set.
/// Regression: the original code unconditionally included the schema, producing invalid SQL
/// like "HangFire"."Job" on Firebird and SQLite.
/// </summary>
public class BuildWrappedTableNameTests
{
    [Table("Job", "HangFire")]
    private class SchemaEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void BuildCreate_Firebird_DropsSchemaPrefix_WhenDialectDoesNotSupportNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOB", sql, StringComparison.OrdinalIgnoreCase); // Firebird uppercases
    }

    [Fact]
    public void BuildCreate_Sqlite_DropsSchemaPrefix_WhenDialectDoesNotSupportNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCreate_PostgreSql_IncludesSchemaPrefix_WhenDialectSupportsNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCreate_SqlServer_IncludesSchemaPrefix_WhenDialectSupportsNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", sql, StringComparison.OrdinalIgnoreCase);
    }
}
