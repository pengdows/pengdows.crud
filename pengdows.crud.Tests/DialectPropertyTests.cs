using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectPropertyTests
{
    public static IEnumerable<object[]> DialectData()
    {
        yield return new object[] { SupportedDatabase.Firebird };
        yield return new object[] { SupportedDatabase.MySql };
        yield return new object[] { SupportedDatabase.Oracle };
        yield return new object[] { SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.Sqlite };
        yield return new object[] { SupportedDatabase.DuckDB };
    }

    private static DialectPropertyConfig BuildConfig(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db.ToString());
        var logger = NullLogger.Instance;
        return db switch
        {
            SupportedDatabase.Firebird => new DialectPropertyConfig(
                new FirebirdDialect(factory, logger),
                new DialectProps(
                    SupportedDatabase.Firebird,
                    "@",
                    true,
                    65535,
                    63,
                    ProcWrappingStyle.ExecuteProcedure,
                    "\"",
                    "\"",
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    true,
                    false,
                    true)),
            SupportedDatabase.MySql => new DialectPropertyConfig(
                new MySqlDialect(factory, logger),
                new DialectProps(
                    SupportedDatabase.MySql,
                    "@",
                    true,
                    65535,
                    64,
                    ProcWrappingStyle.Call,
                    "\"",
                    "\"",
                    false,
                    true,
                    false,
                    false,
                    false,
                    false,
                    false,
                    true,
                    true)),
            SupportedDatabase.Oracle => new DialectPropertyConfig(
                new OracleDialect(factory, logger),
                new DialectProps(
                    SupportedDatabase.Oracle,
                    ":",
                    true,
                    64000,
                    30,
                    ProcWrappingStyle.Oracle,
                    "\"",
                    "\"",
                    false,
                    false,
                    true,
                    false,
                    true,
                    true,
                    true,
                    true,
                    true)),
            SupportedDatabase.PostgreSql => new DialectPropertyConfig(
                new PostgreSqlDialect(factory, logger),
                new DialectProps(
                    SupportedDatabase.PostgreSql,
                    ":",
                    true,
                    32767,
                    63,
                    ProcWrappingStyle.PostgreSQL,
                    "\"",
                    "\"",
                    true,
                    false,
                    false,
                    false,
                    true,
                    true,
                    true,
                    true,
                    true)),
            SupportedDatabase.SqlServer => new DialectPropertyConfig(
                new SqlServerDialect(factory, logger),
                new DialectProps(
                    SupportedDatabase.SqlServer,
                    "@",
                    true,
                    2100,
                    128,
                    ProcWrappingStyle.Exec,
                    "\"",
                    "\"",
                    false,
                    false,
                    false,
                    false,
                    true,
                    true,
                    true,
                    true,
                    true)),
            SupportedDatabase.Sqlite => new DialectPropertyConfig(
                new SqliteDialect(factory, logger),
                new DialectProps(
                    SupportedDatabase.Sqlite,
                    "@",
                    true,
                    999,
                    255,
                    ProcWrappingStyle.None,
                    "\"",
                    "\"",
                    true,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    true)),
            SupportedDatabase.DuckDB => new DialectPropertyConfig(
                new DuckDbDialect(factory, logger),
                new DialectProps(
                    SupportedDatabase.DuckDB,
                    "$",
                    true,
                    65535,
                    255,
                    ProcWrappingStyle.None,
                    "\"",
                    "\"",
                    true,
                    false,
                    false,
                    true,
                    true,
                    true,
                    true,
                    true,
                    false)),
            _ => throw new ArgumentOutOfRangeException(nameof(db), db, null)
        };
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void Dialect_properties_match_expected(SupportedDatabase db)
    {
        var config = BuildConfig(db);
        var dialect = config.Dialect;
        var expected = config.Expected;

        Assert.Equal(expected.DatabaseType, dialect.DatabaseType);
        Assert.NotEqual(SupportedDatabase.Unknown, dialect.DatabaseType);

        Assert.Equal(expected.ParameterMarker, dialect.ParameterMarker);
        Assert.NotEqual(expected.ParameterMarker == "@" ? ":" : "@", dialect.ParameterMarker);

        Assert.Equal(expected.SupportsNamedParameters, dialect.SupportsNamedParameters);
        Assert.NotEqual(!expected.SupportsNamedParameters, dialect.SupportsNamedParameters);

        Assert.Equal(expected.MaxParameterLimit, dialect.MaxParameterLimit);
        Assert.NotEqual(expected.MaxParameterLimit + 1, dialect.MaxParameterLimit);

        Assert.Equal(expected.ParameterNameMaxLength, dialect.ParameterNameMaxLength);
        Assert.NotEqual(expected.ParameterNameMaxLength + 1, dialect.ParameterNameMaxLength);

        Assert.Equal(expected.ProcWrappingStyle, dialect.ProcWrappingStyle);
        var unexpectedProcStyle = expected.ProcWrappingStyle == ProcWrappingStyle.None
            ? ProcWrappingStyle.Call
            : ProcWrappingStyle.None;
        Assert.NotEqual(unexpectedProcStyle, dialect.ProcWrappingStyle);

        Assert.Equal(expected.QuotePrefix, dialect.QuotePrefix);
        switch (expected.QuotePrefix)
        {
            case "\"":
                Assert.NotEqual("`", dialect.QuotePrefix);
                break;
            default:
                Assert.NotEqual("\"", dialect.QuotePrefix);
                break;
        }

        Assert.Equal(expected.QuoteSuffix, dialect.QuoteSuffix);
        switch (expected.QuoteSuffix)
        {
            case "\"":
                Assert.NotEqual("`", dialect.QuoteSuffix);
                break;
            default:
                Assert.NotEqual("\"", dialect.QuoteSuffix);
                break;
        }

        Assert.Equal(expected.SupportsInsertOnConflict, dialect.SupportsInsertOnConflict);
        Assert.NotEqual(!expected.SupportsInsertOnConflict, dialect.SupportsInsertOnConflict);

        Assert.Equal(expected.SupportsOnDuplicateKey, dialect.SupportsOnDuplicateKey);
        Assert.NotEqual(!expected.SupportsOnDuplicateKey, dialect.SupportsOnDuplicateKey);

        Assert.Equal(expected.SupportsMerge, dialect.SupportsMerge);
        Assert.NotEqual(!expected.SupportsMerge, dialect.SupportsMerge);

        Assert.Equal(expected.SupportsJsonTypes, dialect.SupportsJsonTypes);
        Assert.NotEqual(!expected.SupportsJsonTypes, dialect.SupportsJsonTypes);

        Assert.Equal(expected.SupportsWindowFunctions, dialect.SupportsWindowFunctions);
        Assert.NotEqual(!expected.SupportsWindowFunctions, dialect.SupportsWindowFunctions);

        Assert.Equal(expected.SupportsCommonTableExpressions, dialect.SupportsCommonTableExpressions);
        Assert.NotEqual(!expected.SupportsCommonTableExpressions, dialect.SupportsCommonTableExpressions);

        Assert.Equal(expected.SupportsArrayTypes, dialect.SupportsArrayTypes);
        Assert.NotEqual(!expected.SupportsArrayTypes, dialect.SupportsArrayTypes);

        Assert.Equal(expected.SupportsNamespaces, dialect.SupportsNamespaces);
        Assert.NotEqual(!expected.SupportsNamespaces, dialect.SupportsNamespaces);

        Assert.Equal(expected.SupportsSavepoints, dialect.SupportsSavepoints);
        Assert.NotEqual(!expected.SupportsSavepoints, dialect.SupportsSavepoints);
    }

    public record DialectProps(
        SupportedDatabase DatabaseType,
        string ParameterMarker,
        bool SupportsNamedParameters,
        int MaxParameterLimit,
        int ParameterNameMaxLength,
        ProcWrappingStyle ProcWrappingStyle,
        string QuotePrefix,
        string QuoteSuffix,
        bool SupportsInsertOnConflict,
        bool SupportsOnDuplicateKey,
        bool SupportsMerge,
        bool SupportsJsonTypes,
        bool SupportsWindowFunctions,
        bool SupportsCommonTableExpressions,
        bool SupportsArrayTypes,
        bool SupportsNamespaces,
        bool SupportsSavepoints);

    private sealed record DialectPropertyConfig(ISqlDialect Dialect, DialectProps Expected);
}