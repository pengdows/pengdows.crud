using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.@internal;

/// <summary>
/// The typed Npgsql readers for interval and inet columns. Each test resolves the provider types through
/// <see cref="NpgsqlProviderStandIns"/> so the Npgsql-only branches run without the Npgsql package.
/// </summary>
public class NpgsqlTypedFieldReaderTests
{
    private static readonly ParameterExpression Record = Expression.Parameter(typeof(IDataRecord), "record");

    private static NpgsqlTypedFieldReader Reader(string npgsqlTypeName, string dataTypeName)
    {
        var ctor = typeof(NpgsqlTypedFieldReader).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(string), typeof(string) }, null)!;
        return (NpgsqlTypedFieldReader)ctor.Invoke(new object[] { npgsqlTypeName, dataTypeName });
    }

    private static fakeDbDataReader OneRow(object value, string dataTypeName)
    {
        var reader = new fakeDbDataReader(new List<Dictionary<string, object>>
        {
            new() { ["v"] = value }
        })
        {
            Columns = new[] { new fakeDbColumn("v", value.GetType(), dataTypeName) }
        };
        Assert.True(reader.Read());
        return reader;
    }

    private static object? Run(Expression? expression, IDataRecord record)
    {
        Assert.NotNull(expression);
        var body = Expression.Convert(expression!, typeof(object));
        var lambda = Expression.Lambda<Func<IDataRecord, object?>>(body, Record).Compile();
        return lambda(record);
    }

    [Fact]
    public void BuildTypedValue_Interval_ReadsMembersIntoPostgreSqlInterval()
    {
        using var _ = NpgsqlProviderStandIns.Resolve();
        var type = NpgsqlProviderStandIns.Interval;
        var reader = OneRow(NpgsqlProviderStandIns.NewInterval(type, 2, 3, 4_000_000L), "interval");

        var result = Assert.IsType<PostgreSqlInterval>(
            Run(Reader("NpgsqlTypes.NpgsqlInterval, Npgsql", "interval").BuildTypedValue(Record, 0), reader));

        Assert.Equal(2, result.Months);
        Assert.Equal(3, result.Days);
        Assert.Equal(4_000_000L, result.Microseconds);
    }

    [Fact]
    public void BuildTypedValue_Inet_ReadsAddressAndNetmaskIntoInet()
    {
        using var _ = NpgsqlProviderStandIns.Resolve();
        var type = NpgsqlProviderStandIns.Inet;
        var address = IPAddress.Parse("10.0.0.1");
        var reader = OneRow(NpgsqlProviderStandIns.NewInet(type, address, 24), "inet");

        var result = Assert.IsType<Inet>(
            Run(Reader("NpgsqlTypes.NpgsqlInet, Npgsql", "inet").BuildTypedValue(Record, 0), reader));

        Assert.Equal(address, result.Address);
        Assert.Equal((byte)24, result.PrefixLength);
    }

    [Fact]
    public void BuildTypedValue_UnresolvableProviderType_ReturnsNull()
    {
        using var _ = NpgsqlProviderStandIns.Resolve();

        var expression = Reader("NpgsqlTypes.NoSuchType, Npgsql", "x").BuildTypedValue(Record, 0);

        Assert.Null(expression);
    }

    [Fact]
    public void ReadTyped_ReturnsTheProviderValueBoxed()
    {
        using var _ = NpgsqlProviderStandIns.Resolve();
        var type = NpgsqlProviderStandIns.Interval;
        var value = NpgsqlProviderStandIns.NewInterval(type, 1, 1, 1L);
        var reader = OneRow(value, "interval");

        var result = Reader("NpgsqlTypes.NpgsqlInterval, Npgsql", "interval").ReadTyped(reader, 0);

        Assert.Equal(type, result.GetType());
        Assert.Equal(1, type.GetProperty("Months")!.GetValue(result));
    }

    [Fact]
    public void Read_MatchingDataTypeName_UsesTheTypedReader()
    {
        using var _ = NpgsqlProviderStandIns.Resolve();
        var type = NpgsqlProviderStandIns.Interval;
        var reader = OneRow(NpgsqlProviderStandIns.NewInterval(type, 5, 6, 7L), "interval");

        var result = Reader("NpgsqlTypes.NpgsqlInterval, Npgsql", "interval").Read(reader, 0);

        Assert.Equal(type, result.GetType());
        Assert.Equal(5, type.GetProperty("Months")!.GetValue(result));
    }

    [Fact]
    public void Read_DifferentDataTypeName_FallsBackToGetValue()
    {
        using var _ = NpgsqlProviderStandIns.Resolve();
        var type = NpgsqlProviderStandIns.Interval;
        var reader = OneRow(NpgsqlProviderStandIns.NewInterval(type, 9, 9, 9L), "text");

        var result = Reader("NpgsqlTypes.NpgsqlInterval, Npgsql", "interval").Read(reader, 0);

        Assert.Equal(type, result.GetType());
    }

    [Fact]
    public void Read_UnresolvableProviderType_FallsBackToGetValue()
    {
        var reader = OneRow("raw", "interval");

        var result = Reader("NpgsqlTypes.NoSuchType, Npgsql", "interval").Read(reader, 0);

        Assert.Equal("raw", result);
    }

    [Fact]
    public void AppliesTo_NonNpgsqlReader_IsFalse()
    {
        // A fakeDb reader is not an Npgsql reader, so the typed path never applies to it.
        var reader = OneRow("x", "interval");

        Assert.False(Reader("NpgsqlTypes.NpgsqlInterval, Npgsql", "interval").AppliesTo(reader, 0));
    }

    [Fact]
    public void Unwrap_ReturnsTheReaderItself_WhenItIsADbDataReader()
    {
        var reader = OneRow("x", "text");

        Assert.Same(reader, NpgsqlTypedFieldReader.Unwrap(reader));
    }
}
