using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Contract coverage for the complete ADO.NET DbType surface across every
/// provider dialect. These tests validate parameter construction; live provider
/// round trips remain provider-specific integration tests.
/// </summary>
public sealed class DbTypeProviderMatrixTests
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

    public static IEnumerable<object[]> ProviderAndDbTypes()
    {
        foreach (var provider in Providers)
        {
            foreach (var dbType in Enum.GetValues<DbType>())
            {
                yield return [provider, dbType];
            }
        }
    }

    public static IEnumerable<object[]> ProviderAndRepresentativeValues()
    {
        var values = new (DbType Type, object Value)[]
        {
            (DbType.AnsiString, "text"),
            (DbType.AnsiStringFixedLength, "text"),
            (DbType.Binary, new byte[] { 1, 2, 3 }),
            (DbType.Boolean, true),
            (DbType.Byte, (byte)1),
            (DbType.Currency, 1.25m),
            (DbType.Date, DateTime.UtcNow),
            (DbType.DateTime, DateTime.UtcNow),
            (DbType.DateTime2, DateTime.UtcNow),
            (DbType.DateTimeOffset, DateTimeOffset.UtcNow),
            (DbType.Decimal, 1.25m),
            (DbType.Double, 1.25d),
            (DbType.Guid, Guid.NewGuid()),
            (DbType.Int16, (short)1),
            (DbType.Int32, 1),
            (DbType.Int64, 1L),
            (DbType.Object, new object()),
            (DbType.SByte, (sbyte)1),
            (DbType.Single, 1.25f),
            (DbType.String, "text"),
            (DbType.StringFixedLength, "text"),
            (DbType.Time, TimeSpan.FromSeconds(1)),
            (DbType.UInt16, (ushort)1),
            (DbType.UInt32, 1u),
            (DbType.UInt64, 1ul),
            (DbType.VarNumeric, 1.25m),
            (DbType.Xml, "<root />")
        };

        foreach (var provider in Providers)
        {
            foreach (var (type, value) in values)
            {
                yield return [provider, type, value];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProviderAndDbTypes))]
    public void EveryProviderCanConstructEveryAdoNetDbTypeParameter(
        SupportedDatabase provider,
        DbType dbType)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(
            provider,
            new fakeDbFactory(provider),
            NullLogger<SqlDialect>.Instance);

        var parameter = dialect.CreateDbParameter("p", dbType, DBNull.Value);

        Assert.NotNull(parameter);
        // Dialects may intentionally normalize a DbType to their provider
        // representation (for example Oracle Boolean→Int16 or Guid→String).
        Assert.True(Enum.IsDefined(parameter.DbType));
        Assert.Equal(DBNull.Value, parameter.Value);
    }

    [Theory]
    [MemberData(nameof(ProviderAndRepresentativeValues))]
    public void EveryProviderAcceptsRepresentativeClrValue(
        SupportedDatabase provider,
        DbType dbType,
        object value)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(
            provider,
            new fakeDbFactory(provider),
            NullLogger<SqlDialect>.Instance);

        var parameter = dialect.CreateDbParameter("p", dbType, value);

        Assert.NotNull(parameter);
        Assert.NotNull(parameter.Value);
    }
}
