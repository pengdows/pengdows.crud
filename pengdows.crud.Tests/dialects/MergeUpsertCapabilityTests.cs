using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Moq;
using pengdows.crud.@internal;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// The gateways choose their upsert shape from dialect capabilities, never from
/// <c>Product == SupportedDatabase.Firebird</c>. Firebird's MERGE support is
/// <c>UPDATE OR INSERT ... MATCHING</c>, not ANSI MERGE (<c>EmitsAnsiMergeSyntax</c> false), and
/// that statement needs no SET clause, so it alone upserts an entity with only key columns
/// (<c>SupportsPureKeyUpsert</c> true).
/// </summary>
public class MergeUpsertCapabilityTests
{
    public static TheoryData<SupportedDatabase> Products() => new()
    {
        SupportedDatabase.SqlServer, SupportedDatabase.PostgreSql, SupportedDatabase.AuroraPostgreSql,
        SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb, SupportedDatabase.MySql,
        SupportedDatabase.AuroraMySql, SupportedDatabase.MariaDb, SupportedDatabase.TiDb,
        SupportedDatabase.SingleStore, SupportedDatabase.Oracle, SupportedDatabase.Sqlite,
        SupportedDatabase.DuckDB, SupportedDatabase.Firebird, SupportedDatabase.Snowflake,
        SupportedDatabase.Db2, SupportedDatabase.Informix, SupportedDatabase.SybaseASE,
        SupportedDatabase.Spanner, SupportedDatabase.SapHana, SupportedDatabase.InterBase,
        SupportedDatabase.FlatFile, SupportedDatabase.Access
    };

    [Theory]
    [MemberData(nameof(Products))]
    public void EmitsAnsiMergeSyntax_IsFalseOnlyForFirebird(SupportedDatabase product)
    {
        Assert.Equal(product != SupportedDatabase.Firebird, Dialect(product).EmitsAnsiMergeSyntax());
    }

    [Theory]
    [MemberData(nameof(Products))]
    public void SupportsPureKeyUpsert_IsTrueOnlyForFirebird(SupportedDatabase product)
    {
        Assert.Equal(product == SupportedDatabase.Firebird, Dialect(product).SupportsPureKeyUpsert());
    }

    [Theory]
    [MemberData(nameof(Products))]
    public void RequiresOutputParameterForReturning_IsTrueOnlyForOracle(SupportedDatabase product)
    {
        Assert.Equal(product == SupportedDatabase.Oracle, Dialect(product).RequiresOutputParameterForReturning());
    }

    [Fact]
    public void RenderOutputInsertClauses_SqlServer_RoutesThroughATableVariable()
    {
        // A plain OUTPUT INSERTED.col breaks when the table has an enabled trigger, so SQL Server
        // captures the id INTO a table variable and selects it after the insert.
        var dialect = (IInternalSqlDialect)Dialect(SupportedDatabase.SqlServer);

        var (prefix, output, returning) = dialect.RenderOutputInsertClauses("\"id\"", "OUTPUT INSERTED.\"id\"");

        Assert.Equal("DECLARE @__pengdows_output TABLE (\"id\" sql_variant); ", prefix);
        Assert.Equal("OUTPUT INSERTED.\"id\" INTO @__pengdows_output (\"id\")", output);
        Assert.Equal("; SELECT \"id\" FROM @__pengdows_output", returning);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Oracle)]
    public void RenderOutputInsertClauses_OtherDialects_PutTheWholeClauseBeforeValues(SupportedDatabase product)
    {
        var dialect = (IInternalSqlDialect)Dialect(product);

        Assert.Equal((string.Empty, "OUTPUT x", string.Empty), dialect.RenderOutputInsertClauses("\"id\"", "OUTPUT x"));
    }

    [Fact]
    public void Gateways_NeverCompareTheDatabaseProduct()
    {
        // Per-database behavior belongs on the dialect as a capability. A product comparison in a
        // gateway silently does the wrong thing for the next database that shares the behavior.
        var root = FindRepositoryRoot();
        var offenders = Directory.GetFiles(Path.Combine(root, "pengdows.crud"), "*Gateway*.cs")
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, index))
                .Where(x => Regex.IsMatch(x.line, @"(Product|DatabaseType)\s*[!=]=\s*SupportedDatabase\.")))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.index + 1}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Product checks in gateways:\n" + string.Join("\n", offenders));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "pengdows.crud.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    [Fact]
    public void NonInternalDialect_DefaultsToAnsiMergeWithoutPureKeyUpsert()
    {
        var dialect = new Mock<ISqlDialect>().Object;

        Assert.True(dialect.EmitsAnsiMergeSyntax());
        Assert.False(dialect.SupportsPureKeyUpsert());
        Assert.False(dialect.RequiresOutputParameterForReturning());
    }

    private static ISqlDialect Dialect(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        return context.GetDialect();
    }
}
