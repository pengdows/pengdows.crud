#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

// Targets specific branches in pengdows.crud.@internal.CompiledMapperFactory<TEntity> that were
// previously unreachable through the existing test suite (CompiledMapperOptimizationTests.cs is
// misleadingly named - it actually exercises DataReaderMapper.LoadAsync, an entirely independent
// hydration implementation that never calls CompiledMapperFactory; TypeCoercionAndCompiledMapperEdgeCaseTests.cs
// only reflection-tests GetReaderMethod/IsNumericType). These tests go through the real production
// path (TableGateway.RetrieveOneAsync/RetrieveAsync -> BaseTableGateway.Reader.cs -> CompiledMapperFactory)
// against a fakeDb-backed reader, per this repo's stated testing convention.
public class CompiledMapperFactoryUncoveredBranchTests
{
    private static (fakeDbFactory factory, TypeMapRegistry typeMap) MakeFactory(params Dictionary<string, object?>[] rows)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        execConn.EnqueueReaderResult(rows);
        factory.Connections.Add(execConn);
        return (factory, new TypeMapRegistry());
    }

    // ------------------------------------------------------------------ //
    //  Native JSON CLR types: JsonValue / JsonDocument / JsonElement
    //  (CompiledMapperFactory.cs lines ~76-93, CoerceNativeJson ~260-286)
    // ------------------------------------------------------------------ //

    [Table("JVEntities")]
    private class JsonValueEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("Payload", DbType.String)] public JsonValue Payload { get; set; }
    }

    [Fact]
    public async Task RetrieveOneAsync_JsonValueColumn_ParsesRawStringIntoJsonValue()
    {
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["Payload"] = "{\"a\":1}" });
        typeMap.Register<JsonValueEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<JsonValueEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.Equal("{\"a\":1}", result!.Payload.ToString());
    }

    [Table("JDEntities")]
    private class JsonDocumentEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("Payload", DbType.String)] public JsonDocument Payload { get; set; } = null!;
    }

    [Fact]
    public async Task RetrieveOneAsync_JsonDocumentColumn_ParsesRawStringIntoJsonDocument()
    {
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["Payload"] = "{\"b\":2}" });
        typeMap.Register<JsonDocumentEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<JsonDocumentEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Payload.RootElement.GetProperty("b").GetInt32());
    }

    [Table("JEEntities")]
    private class JsonElementEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("Payload", DbType.String)] public JsonElement Payload { get; set; }
    }

    [Fact]
    public async Task RetrieveOneAsync_JsonElementColumn_ParsesRawStringIntoJsonElement()
    {
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["Payload"] = "{\"c\":3}" });
        typeMap.Register<JsonElementEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<JsonElementEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Payload.GetProperty("c").GetInt32());
    }

    [Fact]
    public async Task RetrieveOneAsync_JsonValueColumn_RawValueAlreadyJsonElement_ReturnsAsIs()
    {
        // Covers CoerceNativeJson's early-return branch: runtimeTarget.IsInstanceOfType(value) == true,
        // i.e. the raw provider value is already the exact target CLR type - no parse needed.
        using var preParsed = JsonDocument.Parse("{\"d\":4}");
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["Payload"] = preParsed.RootElement });
        typeMap.Register<JsonElementEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<JsonElementEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.Equal(4, result!.Payload.GetProperty("d").GetInt32());
    }

    // ------------------------------------------------------------------ //
    //  byte[] -> Guid? (nullable) fast path (CompiledMapperFactory.cs line ~117)
    // ------------------------------------------------------------------ //

    [Table("NullableGuidEntities")]
    private class NullableGuidFromBytesEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Column("GuidBytes", DbType.Binary)] public Guid? GuidBytes { get; set; }
    }

    [Fact]
    public async Task RetrieveOneAsync_ByteArrayColumn_MapsToNullableGuidProperty()
    {
        var guid = Guid.NewGuid();
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["GuidBytes"] = guid.ToByteArray() });
        typeMap.Register<NullableGuidFromBytesEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<NullableGuidFromBytesEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.Equal(guid, result!.GuidBytes);
    }

    // ------------------------------------------------------------------ //
    //  BuildConversionExpression: long -> bool, string -> Guid
    //  (CompiledMapperFactory.cs lines ~359-362 and ~367-371)
    // ------------------------------------------------------------------ //

    [Table("LongToBoolEntities")]
    private class LongToBoolEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Column("Flag", DbType.Int64)] public bool Flag { get; set; }
    }

    [Fact]
    public async Task RetrieveOneAsync_LongColumn_CoercesToBoolProperty_NonZeroIsTrue()
    {
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["Flag"] = 1L });
        typeMap.Register<LongToBoolEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<LongToBoolEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.True(result!.Flag);
    }

    [Fact]
    public async Task RetrieveOneAsync_LongColumn_CoercesToBoolProperty_ZeroIsFalse()
    {
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["Flag"] = 0L });
        typeMap.Register<LongToBoolEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<LongToBoolEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.False(result!.Flag);
    }

    [Table("StringToGuidEntities")]
    private class StringToGuidEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Column("GuidText", DbType.String)] public Guid GuidText { get; set; }
    }

    [Fact]
    public async Task RetrieveOneAsync_StringColumn_CoercesToGuidProperty_ViaFastParsePath()
    {
        var guid = Guid.NewGuid();
        var (factory, typeMap) = MakeFactory(
            new Dictionary<string, object?> { ["Id"] = 1, ["GuidText"] = guid.ToString() });
        typeMap.Register<StringToGuidEntity>();

        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        var helper = new TableGateway<StringToGuidEntity, int>(ctx);

        var result = await helper.RetrieveOneAsync(1);

        Assert.NotNull(result);
        Assert.Equal(guid, result!.GuidText);
    }
}
