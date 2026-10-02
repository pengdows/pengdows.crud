using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-003, found live 2026-09-30: Npgsql ("DbType.SByte isn't supported"), SqlClient ("No mapping
/// exists from DbType SByte"), ODP.NET, Informix.Net.Core and the PostgreSQL family reject the
/// signed-byte and unsigned DbTypes outright, so an sbyte/ushort/uint/ulong property could not even
/// build its INSERT. Those dialects bind them as the smallest signed type that holds the full range;
/// dialects whose providers take them natively (MySQL family, Firebird, DuckDB, FlatFile, Sybase ASE)
/// are unchanged.
/// </summary>
public sealed class UnsignedDbTypeWideningTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

    public static TheoryData<SupportedDatabase> Widening() => new()
    {
        SupportedDatabase.PostgreSql, SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb,
        SupportedDatabase.Spanner, SupportedDatabase.SqlServer, SupportedDatabase.Oracle,
        SupportedDatabase.Informix, SupportedDatabase.Db2, SupportedDatabase.SapHana
    };

    [Theory]
    [MemberData(nameof(Widening))]
    public void SByteAndUnsigned_BindAsTheSmallestSignedTypeThatHoldsTheRange(SupportedDatabase database)
    {
        var dialect = Dialect(database);

        AssertBinding(dialect.CreateDbParameter("p", DbType.SByte, sbyte.MinValue), DbType.Int16, (short)sbyte.MinValue);
        AssertBinding(dialect.CreateDbParameter("p", DbType.UInt16, ushort.MaxValue), DbType.Int32, (int)ushort.MaxValue);
        AssertBinding(dialect.CreateDbParameter("p", DbType.UInt32, uint.MaxValue), DbType.Int64, (long)uint.MaxValue);
        AssertBinding(dialect.CreateDbParameter("p", DbType.UInt64, ulong.MaxValue), DbType.Decimal, (decimal)ulong.MaxValue);
        AssertBinding(dialect.CreateDbParameter<ulong?>("p", DbType.UInt64, null), DbType.Decimal, DBNull.Value);
    }

    // IBM's Db2 driver treats DbType.Byte as binary data and casts the value to byte[] (InvalidCast
    // "System.Byte to System.Byte[]", confirmed live), so a byte binds as Int16 there; elsewhere
    // DbType.Byte is the provider's tinyint and is left alone.
    [Fact]
    public void Db2_BindsByteAsInt16()
    {
        AssertBinding(Dialect(SupportedDatabase.Db2).CreateDbParameter("p", DbType.Byte, byte.MaxValue), DbType.Int16, (short)byte.MaxValue);
        AssertBinding(Dialect(SupportedDatabase.Db2).CreateDbParameter<byte?>("p", DbType.Byte, null), DbType.Int16, DBNull.Value);
        Assert.Equal(DbType.Byte, Dialect(SupportedDatabase.SqlServer).CreateDbParameter("p", DbType.Byte, byte.MaxValue).DbType);
    }

    // Microsoft.Data.Sqlite takes UInt64 but casts it to Int64 unchecked (ulong.MaxValue was stored as
    // -1, confirmed live), and SQLite's decimal binding goes through double, which can't hold it
    // exactly, so a UInt64 binds as exact text there; the other three widen like everywhere else.
    [Fact]
    public void Sqlite_BindsUInt64AsExactTextAndWidensTheRest()
    {
        var dialect = Dialect(SupportedDatabase.Sqlite);

        AssertBinding(dialect.CreateDbParameter("p", DbType.UInt64, ulong.MaxValue), DbType.String, "18446744073709551615");
        AssertBinding(dialect.CreateDbParameter("p", DbType.SByte, sbyte.MinValue), DbType.Int16, (short)sbyte.MinValue);
        AssertBinding(dialect.CreateDbParameter("p", DbType.UInt32, uint.MaxValue), DbType.Int64, (long)uint.MaxValue);
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void DialectsWhoseProvidersTakeThemNatively_AreUnchanged(SupportedDatabase database)
    {
        var parameter = Dialect(database).CreateDbParameter("p", DbType.UInt64, ulong.MaxValue);

        Assert.Equal(DbType.UInt64, parameter.DbType);
        Assert.Equal(ulong.MaxValue, parameter.Value);
    }

    private static void AssertBinding(System.Data.Common.DbParameter parameter, DbType type, object value)
    {
        Assert.Equal(type, parameter.DbType);
        Assert.Equal(value, parameter.Value);
    }
}
