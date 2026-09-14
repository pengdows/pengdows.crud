using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqliteDialectGuidAndViolationTests
{
    private static SqliteDialect Dialect() =>
        new(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance);

    [Fact]
    public void CreateDbParameter_Guid_IsSerializedAsHyphenatedString()
    {
        var dialect = Dialect();
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");

        var param = dialect.CreateDbParameter("p", DbType.Guid, guid);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", param.Value?.ToString());
    }

    [Fact]
    public void IsUniqueViolation_NullException_ReturnsFalseRatherThanThrowing()
    {
        // IsUniqueViolation(DbException) is declared with a non-nullable DbException parameter,
        // but its own "ex is not DbException dbEx" pattern-match guard only actually matches when
        // ex is null (a statically-typed DbException can never fail an "is DbException" check
        // otherwise) — exercising that guard requires passing null explicitly.
        Assert.False(Dialect().IsUniqueViolation(null!));
    }
}
