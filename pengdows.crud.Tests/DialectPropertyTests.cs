using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectPropertyTests
{
    public static IEnumerable<object[]> DialectData()
    {
         var logger = NullLogger.Instance;

        yield return new object[]
        {
            new FirebirdDialect(new FakeDbFactory(SupportedDatabase.Firebird), logger),
            new DialectProps(
                SupportedDatabase.Firebird,
                "@",
                true,
                65535,
                63,
                ProcWrappingStyle.Call,
                "\"",
                "\"",
                false,
                false,
                false,
                false,
                false,
                true,
                true)
        };

        yield return new object[]
        {
            new MySqlDialect(new FakeDbFactory(SupportedDatabase.MySql), logger),
            new DialectProps(
                SupportedDatabase.MySql,
                "@",
                true,
                65535,
                64,
                ProcWrappingStyle.Call,
                "`",
                "`",
                true,
                false,
                false,
                false,
                false,
                false,
                true)
        };

        yield return new object[]
        {
            new OracleDialect(new FakeDbFactory(SupportedDatabase.Oracle), logger),
            new DialectProps(
                SupportedDatabase.Oracle,
                ":",
                true,
                1000,
                30,
                ProcWrappingStyle.Oracle,
                "\"",
                "\"",
                false,
                true,
                false,
                true,
                true,
                true,
                true)
        };

        yield return new object[]
        {
            new PostgreSqlDialect(new FakeDbFactory(SupportedDatabase.PostgreSql), logger),
            new DialectProps(
                SupportedDatabase.PostgreSql,
                ":",
                true,
                65535,
                63,
                ProcWrappingStyle.PostgreSQL,
                "\"",
                "\"",
                true,
                false,
                false,
                true,
                true,
                true,
                true)
        };

        yield return new object[]
        {
            new SqlServerDialect(new FakeDbFactory(SupportedDatabase.SqlServer), logger),
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
                true,
                false,
                true,
                true,
                true,
                true)
        };

        yield return new object[]
        {
            new SqliteDialect(new FakeDbFactory(SupportedDatabase.Sqlite), logger),
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
                false)
        };
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void Dialect_properties_match_expected(SqlDialect dialect, DialectProps expected)
    {
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
        Assert.NotEqual(ProcWrappingStyle.None, dialect.ProcWrappingStyle);

        Assert.Equal(expected.QuotePrefix, dialect.QuotePrefix);
        Assert.NotEqual(expected.QuotePrefix == "\"" ? "[" : "\"", dialect.QuotePrefix);

        Assert.Equal(expected.QuoteSuffix, dialect.QuoteSuffix);
        Assert.NotEqual(expected.QuoteSuffix == "\"" ? "]" : "\"", dialect.QuoteSuffix);

        Assert.Equal(expected.SupportsInsertOnConflict, dialect.SupportsInsertOnConflict);
        Assert.NotEqual(!expected.SupportsInsertOnConflict, dialect.SupportsInsertOnConflict);

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
        bool SupportsMerge,
        bool SupportsJsonTypes,
        bool SupportsWindowFunctions,
        bool SupportsCommonTableExpressions,
        bool SupportsArrayTypes,
        bool SupportsNamespaces);
}

