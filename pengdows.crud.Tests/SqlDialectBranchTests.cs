using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectBranchTests
{
    [Theory]
    [InlineData(SupportedDatabase.MySql, true)]
    [InlineData(SupportedDatabase.MariaDb, true)]
    [InlineData(SupportedDatabase.Sqlite, true)]
    [InlineData(SupportedDatabase.SqlServer, true)]
    [InlineData(SupportedDatabase.PostgreSql, false)]
    [InlineData(SupportedDatabase.DuckDB, false)]
    [InlineData(SupportedDatabase.Oracle, false)]
    [InlineData(SupportedDatabase.Unknown, false)]
    public void HasSessionScopedLastIdFunction_MatchesExpectations(SupportedDatabase db, bool expected)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(expected, dialect.HasSessionScopedLastIdFunction());
    }

    [Theory]
    [InlineData(SupportedDatabase.Oracle, GeneratedKeyPlan.Returning)]
    [InlineData(SupportedDatabase.SqlServer, GeneratedKeyPlan.OutputInserted)]
    [InlineData(SupportedDatabase.PostgreSql, GeneratedKeyPlan.Returning)]
    [InlineData(SupportedDatabase.MySql, GeneratedKeyPlan.SessionScopedFunction)]
    [InlineData(SupportedDatabase.Sqlite, GeneratedKeyPlan.SessionScopedFunction)]
    [InlineData(SupportedDatabase.DuckDB, GeneratedKeyPlan.Returning)]
    [InlineData(SupportedDatabase.Unknown, GeneratedKeyPlan.CorrelationToken)]
    public void GetGeneratedKeyPlan_ReturnsExpected(SupportedDatabase db, GeneratedKeyPlan expected)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(expected, dialect.GetGeneratedKeyPlan());
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, " RETURNING \"id\"")]
    [InlineData(SupportedDatabase.SqlServer, " OUTPUT INSERTED.\"id\"")]
    [InlineData(SupportedDatabase.Sqlite, " RETURNING \"id\"")]
    [InlineData(SupportedDatabase.Oracle, " RETURNING \"id\" INTO :1")]
    [InlineData(SupportedDatabase.Firebird, " RETURNING \"id\"")]
    [InlineData(SupportedDatabase.DuckDB, "")]
    public void RenderInsertReturningClause_UsesProviderSyntax(SupportedDatabase db, string expected)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(expected, dialect.RenderInsertReturningClause("\"id\""));
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, "USING (VALUES (@i0, @i1)) AS s (\"id\", \"name\")")]
    [InlineData(SupportedDatabase.Oracle, "USING (SELECT :i0 AS \"id\", :i1 AS \"name\" FROM DUAL) s")]
    public void RenderMergeSource_UsesProviderSyntax(SupportedDatabase db, string expected)
    {
        var dialect = CreateDialect(db);
        var columns = GetMergeColumns();
        var result = dialect.RenderMergeSource(columns, new[] { "i0", "i1" });

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, "t.\"id\" = s.\"id\"", "t.\"id\" = s.\"id\"")]
    [InlineData(SupportedDatabase.Oracle, "t.\"id\" = s.\"id\"", "(t.\"id\" = s.\"id\")")]
    public void RenderMergeOnClause_UsesProviderSyntax(SupportedDatabase db, string predicate, string expected)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(expected, dialect.RenderMergeOnClause(predicate));
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_RespectsProviderRules()
    {
        var columns = new List<string> { "name" };
        var parameters = new List<string> { "@name" };

        var sqlServer = CreateDialect(SupportedDatabase.SqlServer);
        var sqlServerQuery = sqlServer.GetNaturalKeyLookupQuery("people", "id", columns, parameters);
        Assert.Contains("SELECT TOP 1", sqlServerQuery);
        Assert.Contains("ORDER BY", sqlServerQuery);

        var postgres = CreateDialect(SupportedDatabase.PostgreSql);
        var postgresQuery = postgres.GetNaturalKeyLookupQuery("people", "id", columns, parameters);
        Assert.Contains("LIMIT 1", postgresQuery);

        var oracle = CreateDialect(SupportedDatabase.Oracle);
        var oracleQuery = oracle.GetNaturalKeyLookupQuery("people", "id", columns, parameters);
        Assert.Contains("FETCH FIRST 1 ROWS ONLY", oracleQuery);
        Assert.DoesNotContain("ROWNUM", oracleQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_ValidatesInputs()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);

        Assert.Throws<ArgumentException>(() =>
            dialect.GetNaturalKeyLookupQuery("people", "id", new List<string> { "a" }, new List<string>()));

        Assert.Throws<InvalidOperationException>(() =>
            dialect.GetNaturalKeyLookupQuery("people", "id", new List<string>(), new List<string>()));
    }

    private static ISqlDialect CreateDialect(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var logger = NullLoggerFactory.Instance.CreateLogger("SqlDialect");
        return SqlDialectFactory.CreateDialectForType(db, factory, logger);
    }

    private static IReadOnlyList<IColumnInfo> GetMergeColumns()
    {
        var registry = new TypeMapRegistry();
        var tableInfo = registry.GetTableInfo<MergeTestEntity>();
        return new[] { tableInfo.OrderedColumns[0], tableInfo.OrderedColumns[1] };
    }

    [Table("merge_test")]
    private sealed class MergeTestEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
