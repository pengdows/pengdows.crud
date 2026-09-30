using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Review 2026-09-29: TableGateway chose Db2's "SELECT id FROM FINAL TABLE (INSERT ...)" shape by
/// checking DatabaseType == Db2, which the project rules forbid outside dialects. It is a dialect
/// capability: only Db2 returns a generated key by wrapping the whole INSERT. (3.0 names it
/// WrapsInsertStatementForReturning; 2.0.6 InsertReturningWrapsEntireStatement.)
/// </summary>
public sealed class InsertReturningWrapsStatementTests
{
    public static IEnumerable<object[]> AllDatabases() =>
        Enum.GetValues<SupportedDatabase>().Where(d => d != SupportedDatabase.Unknown).Select(d => new object[] { d });

    [Theory]
    [MemberData(nameof(AllDatabases))]
    public void WrapsInsertStatementForReturning_OnlyForDb2(SupportedDatabase database)
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

        Assert.Equal(database == SupportedDatabase.Db2, dialect.WrapsInsertStatementForReturning);
    }
}
