using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class FlatFileDialectTests
{
    private static FlatFileDialect Dialect() =>
        new(new fakeDbFactory(SupportedDatabase.FlatFile), NullLogger<FlatFileDialect>.Instance);

    [Fact]
    public void CreateDialectForType_FlatFile_ReturnsFlatFileDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.FlatFile);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.FlatFile,
            factory,
            NullLogger<SqlDialect>.Instance);

        Assert.IsType<FlatFileDialect>(dialect);
        Assert.Equal(SupportedDatabase.FlatFile, dialect.DatabaseType);
    }

    [Fact]
    public void SupportsNamedParameters_IsTrue()
        => Assert.True(Dialect().SupportsNamedParameters);

    [Fact]
    public void ParameterMarker_IsColon()
        => Assert.Equal(":", Dialect().ParameterMarker);

    [Fact]
    public void MakeParameterName_UsesColonPrefix()
        => Assert.Equal(":status", Dialect().MakeParameterName("status"));

    [Fact]
    public void ProcWrappingStyle_IsNone()
        => Assert.Equal(ProcWrappingStyle.None, Dialect().ProcWrappingStyle);

    [Fact]
    public void IsClientServerDatabase_IsFalse()
        => Assert.False(Dialect().IsClientServerDatabase);

    [Fact]
    public void SupportsDropTableIfExists_IsTrue()
        => Assert.True(Dialect().SupportsDropTableIfExists);

    [Fact]
    public async Task GetDatabaseVersionAsync_ReadsConnectionServerVersion()
    {
        await using var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.FlatFile };
        using var tracked = new TrackedConnection(connection);

        var version = await Dialect().GetDatabaseVersionAsync(tracked);

        Assert.Equal(connection.ServerVersion, version);
    }

    // -------------------------------------------------------------------------
    // Capability flags.
    //
    // pengdows.flatfile's ServerVersion is hardcoded "1.0" (not meaningfully versioned - there is
    // only one version of the engine's feature set today). Capability flags are explicitly set
    // to match verified real features: MERGE INTO, CTEs (WITH/WITH RECURSIVE), window functions
    // including NTH_VALUE and full frame-clause support, TRUNCATE TABLE, and JSON_VALUE/JSON_TABLE
    // are all real, tested features (see pengdows.sql/SqlParser.cs, SqlAst.cs's
    // SqlWindowFunction/SqlWindowFrameClause, and BoundPredicateEvaluator's JsonValue handling).
    //
    // Flags are individually configured where flatfile genuinely lacks the feature: no
    // CREATE TYPE/user-defined types, no ARRAY type, no regular-expression predicate (no SIMILAR
    // TO/REGEXP), no XML type, no triggers of any kind (confirmed elsewhere: no stored
    // procedures/triggers at all), and no temporal table versioning (FOR SYSTEM_TIME AS OF -
    // explicitly listed as a gap in pengdows.flatfile/SQL_STANDARDS_STATUS.md) or row pattern
    // matching (MATCH_RECOGNIZE - not implemented at all).
    // -------------------------------------------------------------------------


    [Fact]
    public void SupportsMerge_IsTrue()
        => Assert.True(Dialect().SupportsMerge);

    [Fact]
    public void SupportsCommonTableExpressions_IsTrue()
        => Assert.True(Dialect().SupportsCommonTableExpressions);

    [Fact]
    public void SupportsWindowFunctions_IsTrue()
        => Assert.True(Dialect().SupportsWindowFunctions);

    [Fact]
    public void SupportsEnhancedWindowFunctions_IsTrue()
        => Assert.True(Dialect().SupportsEnhancedWindowFunctions);

    [Fact]
    public void SupportsTruncateTable_IsTrue()
        => Assert.True(Dialect().SupportsTruncateTable);

    [Fact]
    public void SupportsJsonTypes_IsTrue()
        => Assert.True(Dialect().SupportsJsonTypes);

    [Fact]
    public void SupportsUserDefinedTypes_IsFalse()
        => Assert.False(Dialect().SupportsUserDefinedTypes);

    [Fact]
    public void SupportsArrayTypes_IsFalse()
        => Assert.False(Dialect().SupportsArrayTypes);

    [Fact]
    public void SupportsRegularExpressions_IsFalse()
        => Assert.False(Dialect().SupportsRegularExpressions);

    [Fact]
    public void SupportsXmlTypes_IsFalse()
        => Assert.False(Dialect().SupportsXmlTypes);

    [Fact]
    public void SupportsInsteadOfTriggers_IsFalse()
        => Assert.False(Dialect().SupportsInsteadOfTriggers);

    [Fact]
    public void SupportsTemporalData_IsFalse()
        => Assert.False(Dialect().SupportsTemporalData);

    [Fact]
    public void SupportsRowPatternMatching_IsFalse()
        => Assert.False(Dialect().SupportsRowPatternMatching);

    [Fact]
    public void SupportsMultidimensionalArrays_IsFalse()
        => Assert.False(Dialect().SupportsMultidimensionalArrays);

    [Fact]
    public void SupportsPropertyGraphQueries_IsFalse()
        => Assert.False(Dialect().SupportsPropertyGraphQueries);

    /// <summary>
    /// Verified against pengdows.sql/SqlParser.cs's ParseMerge: the WHEN MATCHED THEN UPDATE SET
    /// clause parses a bare unqualified column name (ExpectIdentifier then Expect(Equals)) - a
    /// "t.column = ..." qualified target fails with "Expected token 'Equals' ... but found 'Dot'."
    /// Same divergence as PostgreSQL/DuckDB (see their own MergeUpdateRequiresTargetAlias
    /// overrides), unlike the SqlDialect base default (true, for SQL Server/Oracle-style MERGE).
    /// </summary>
    [Fact]
    public void MergeUpdateRequiresTargetAlias_IsFalse()
        => Assert.False(Dialect().MergeUpdateRequiresTargetAlias);
}
