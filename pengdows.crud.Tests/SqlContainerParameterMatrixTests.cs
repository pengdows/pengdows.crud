using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// FEAT-009: DbTypeProviderMatrixTests.cs exercises every (provider x representative CLR value)
// combination, but only against SqlDialect.CreateDbParameter directly — it never goes through
// ISqlContainer.AddParameterWithValue/MakeParameterName, the actual public API surface most
// callers use. This file adds that missing dimension: the same representative-value matrix,
// exercised through a real DatabaseContext/ISqlContainer for every provider, confirming the
// parameter is actually added to the container and MakeParameterName produces a real,
// dialect-formatted placeholder for it — not just that the dialect *could* build one in isolation.
public sealed class SqlContainerParameterMatrixTests
{
    private static readonly SupportedDatabase[] Providers =
    [
        SupportedDatabase.PostgreSql,
        SupportedDatabase.SqlServer,
        SupportedDatabase.Oracle,
        SupportedDatabase.Firebird,
        SupportedDatabase.CockroachDb,
        SupportedDatabase.MariaDb,
        SupportedDatabase.MySql,
        SupportedDatabase.Sqlite,
        SupportedDatabase.DuckDB,
        SupportedDatabase.YugabyteDb,
        SupportedDatabase.TiDb,
        SupportedDatabase.Snowflake,
        SupportedDatabase.AuroraMySql,
        SupportedDatabase.AuroraPostgreSql,
        SupportedDatabase.Db2
    ];

    public static IEnumerable<object[]> ProviderAndRepresentativeValues()
    {
        var values = new (DbType Type, object Value)[]
        {
            (DbType.AnsiString, "text"),
            (DbType.Binary, new byte[] { 1, 2, 3 }),
            (DbType.Boolean, true),
            (DbType.Byte, (byte)1),
            (DbType.Currency, 1.25m),
            (DbType.DateTime, DateTime.UtcNow),
            (DbType.DateTimeOffset, DateTimeOffset.UtcNow),
            (DbType.Decimal, 1.25m),
            (DbType.Double, 1.25d),
            (DbType.Guid, Guid.NewGuid()),
            (DbType.Int16, (short)1),
            (DbType.Int32, 1),
            (DbType.Int64, 1L),
            (DbType.String, "text")
        };

        foreach (var provider in Providers)
        {
            foreach (var (type, value) in values)
            {
                yield return [provider, type, value];
            }
        }
    }

    private static DatabaseContext CreateContext(SupportedDatabase provider)
    {
        return new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", new fakeDbFactory(provider));
    }

    [Theory]
    [MemberData(nameof(ProviderAndRepresentativeValues))]
    public void AddParameterWithValue_ThroughRealContainer_ProducesUsableParameterAndPlaceholder(
        SupportedDatabase provider, DbType dbType, object value)
    {
        using var context = CreateContext(provider);
        using var container = context.CreateSqlContainer();

        var parameter = container.AddParameterWithValue("p", dbType, value);

        Assert.NotNull(parameter);
        Assert.NotNull(parameter.Value);
        Assert.Equal(1, container.ParameterCount);

        var placeholder = container.MakeParameterName(parameter);
        Assert.False(string.IsNullOrWhiteSpace(placeholder));

        // The same placeholder, addressed by name instead of by DbParameter, must match — proving
        // MakeParameterName's two overloads (DbParameter vs. string) stay consistent for a
        // parameter that was actually added to this container, for every provider.
        Assert.Equal(placeholder, container.MakeParameterName("p"));
    }
}
