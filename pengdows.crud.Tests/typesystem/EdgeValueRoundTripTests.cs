using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Edge values the fidelity cells write and read back on the real databases (TypeFidelityCells, EDGES): an empty
/// collection or bit string, and an IPv6 loopback address, which is a subclass of IPAddress. Each must round-trip as the
/// library writes it.
/// </summary>
public class EdgeValueRoundTripTests
{
    // An empty int[] is written as "" (SQLite's comma-joined text: string.Join of nothing). Blank text can't be read as a
    // number (COR-002), but for a collection its text form is the empty collection; reading it back must give that.
    [Fact]
    public void EmptyCollectionText_ReadsBackAsTheEmptyIntArray()
    {
        var value = TypeCoercionHelper.Coerce("", typeof(string), typeof(int[]));

        Assert.Empty((int[])value!);
    }

    [Fact]
    public void EmptyCollectionText_ReadsBackAsTheEmptyList()
    {
        var value = TypeCoercionHelper.Coerce("", typeof(string), typeof(List<int>));

        Assert.Empty((List<int>)value!);
    }

    // An empty BitArray is written as "" (a zero-length bit string); reading "" must give the empty bit string.
    [Fact]
    public void EmptyBitStringText_ReadsBackAsTheEmptyBitArray()
    {
        var value = TypeCoercionHelper.Coerce("", typeof(string), typeof(BitArray));

        Assert.Equal(0, ((BitArray)value!).Length);
    }

    // Snowflake returns the infinities as "inf" and "-inf" (Single.Parse and Double.Parse reject them); NaN as "NaN".
    [Theory]
    [InlineData("inf", float.PositiveInfinity)]
    [InlineData("-inf", float.NegativeInfinity)]
    [InlineData("Infinity", float.PositiveInfinity)]
    [InlineData("NaN", float.NaN)]
    public void FloatText_InfinityAndNaNSpellings_Read(string text, float expected)
    {
        var value = (float)TypeCoercionHelper.Coerce(text, typeof(string), typeof(float))!;

        Assert.True(float.IsNaN(expected) ? float.IsNaN(value) : value == expected);
    }

    [Theory]
    [InlineData("inf", double.PositiveInfinity)]
    [InlineData("-inf", double.NegativeInfinity)]
    public void DoubleText_InfinitySpellings_Read(string text, double expected)
    {
        var value = (double)TypeCoercionHelper.Coerce(text, typeof(string), typeof(double))!;

        Assert.Equal(expected, value);
    }

    // IPAddress.IPv6Loopback is a ReadOnlyIPAddress, a subclass. The write path must map it as the IPAddress it is,
    // as it maps a MemoryStream as a Stream; a subclass that misses the mapping is bound raw and the provider refuses it.
    // A subclass defined here, so the test doesn't depend on which IPAddress runtime type IPv6Loopback has on a framework.
    private sealed class LoopbackSubclass : IPAddress
    {
        public LoopbackSubclass() : base(new byte[] { 127, 0, 0, 1 })
        {
        }
    }

    // The IPv6 loopback as the write path sends it: its text, which is what SQLite stores and reads back.
    [Fact]
    public void IPv6Loopback_ToProviderValue_OnSqlite_IsItsText()
    {
        var converted = new pengdows.crud.types.converters.IPAddressConverter()
            .ToProviderValue(IPAddress.IPv6Loopback, SupportedDatabase.Sqlite);

        Assert.Equal("::1", converted);
    }

    [Table("ip_rows")]
    public sealed class IpRow
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("address", DbType.Object)] public IPAddress Address { get; set; } = IPAddress.Any;
    }

    // The parameter a write builds for the IPv6 loopback on SQLite, as an object-typed column (the fidelity cell's type):
    // its value must be the address's text, which is what SQLite stores.
    [Fact]
    public async Task IPv6Loopback_ParameterOnSqlite_CarriesItsText()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        await using var sc = context.CreateSqlContainer("SELECT 1");

        var parameter = sc.AddParameterWithValue("address", DbType.Object, IPAddress.IPv6Loopback);

        Assert.Equal("::1", parameter.Value);
    }

    // Microsoft.Data.Sqlite truncates text to the parameter's Size, and a pooled parameter keeps the Size of its last use.
    // A value that becomes text (an IPAddress, through its converter) must size the parameter to its text, as a string does.
    [Fact]
    public async Task TextFromAnObject_SizesTheParameterToItsText()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        await using var sc = context.CreateSqlContainer("SELECT 1");

        var parameter = sc.AddParameterWithValue("address", DbType.Object, IPAddress.Parse("192.168.1.10"));

        Assert.Equal("192.168.1.10".Length, parameter.Size);
    }

    [Fact]
    public void IPAddressSubclass_IsWrittenAsAnIPAddress()
    {
        Assert.Equal(typeof(IPAddress), AdvancedTypeRegistry.WriteType(typeof(LoopbackSubclass)));
    }
}
