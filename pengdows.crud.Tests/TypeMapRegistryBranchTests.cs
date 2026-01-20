using System;
using System.Data;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class TypeMapRegistryBranchTests
{
    private enum SampleEnum
    {
        One = 1,
        Two = 2
    }

    [Table(" ")]
    private sealed class EmptyTableNameEntity
    {
        [Column("id", DbType.Int32)]
        [Id]
        public int Id { get; set; }
    }

    [Table("test_table")]
    private sealed class EmptyColumnNameEntity
    {
        [Column(" ", DbType.Int32)]
        [Id]
        public int Id { get; set; }
    }

    [Table("test_table")]
    private sealed class EnumInvalidDbTypeEntity
    {
        [Column("status", DbType.Binary)]
        public SampleEnum Status { get; set; }
    }

    [Table("test_table")]
    private sealed class JsonVersionEntity
    {
        [Column("id", DbType.Int32)]
        [Id]
        public int Id { get; set; }

        [Column("version", DbType.Int32)]
        [Version]
        [Json]
        public int Version { get; set; }
    }

    [Table("test_table")]
    private sealed class PrimaryKeyGapEntity
    {
        [Column("a", DbType.Int32)]
        [PrimaryKey(1)]
        public int A { get; set; }

        [Column("b", DbType.Int32)]
        [PrimaryKey(3)]
        public int B { get; set; }
    }

    [Table("test_table")]
    private sealed class PrimaryKeyDuplicateEntity
    {
        [Column("a", DbType.Int32)]
        [PrimaryKey(1)]
        public int A { get; set; }

        [Column("b", DbType.Int32)]
        [PrimaryKey(1)]
        public int B { get; set; }
    }

    [Table("test_table")]
    private sealed class NegativeOrdinalEntity
    {
        [Column("a", DbType.Int32, ordinal: -1)]
        [Id]
        public int A { get; set; }
    }

    [Table("test_table")]
    private sealed class DuplicateOrdinalEntity
    {
        [Column("a", DbType.Int32, ordinal: 1)]
        [Id]
        public int A { get; set; }

        [Column("b", DbType.Int32, ordinal: 1)]
        public int B { get; set; }
    }

    [Table("test_table")]
    private sealed class InvalidLastUpdatedOnEntity
    {
        [Column("id", DbType.Int32)]
        [Id]
        public int Id { get; set; }

        [Column("updated_on", DbType.String)]
        [LastUpdatedOn]
        public string UpdatedOn { get; set; } = string.Empty;
    }

    [Table("test_table")]
    private sealed class InvalidCreatedByEntity
    {
        [Column("id", DbType.Int32)]
        [Id]
        public int Id { get; set; }

        [Column("created_by", DbType.Int32)]
        [CreatedBy]
        public int CreatedBy { get; set; }
    }

    [Table("test_table")]
    private sealed class JsonInferenceEntity
    {
        [Column("id", DbType.Int32)]
        [Id]
        public int Id { get; set; }

        [Column("payload", DbType.String)]
        public JsonDocument Payload { get; set; } = JsonDocument.Parse("{}");

        [Column("node", DbType.String)]
        public JsonNode? Node { get; set; }
    }

    [Table("test_table")]
    private sealed class InvalidVersionTypeEntity
    {
        [Column("id", DbType.Int32)]
        [Id]
        public int Id { get; set; }

        [Column("version", DbType.String)]
        [Version]
        public string Version { get; set; } = string.Empty;
    }

    [Table("test_table")]
    private sealed class OrdinalAssignmentEntity
    {
        [Column("a", DbType.Int32, ordinal: 2)]
        [PrimaryKey]
        public int A { get; set; }

        [Column("b", DbType.Int32)]
        [PrimaryKey]
        public int B { get; set; }
    }

    private sealed class TestColumnInfo : IColumnInfo
    {
        public string Name { get; init; } = "version";
        public PropertyInfo PropertyInfo { get; init; } = typeof(TestColumnInfo).GetProperty(nameof(Name))!;
        public bool IsId { get; init; }
        public DbType DbType { get; set; }
        public bool IsNonUpdateable { get; set; }
        public bool IsNonInsertable { get; set; }
        public bool IsEnum { get; set; }
        public Type? EnumType { get; set; }
        public Type? EnumUnderlyingType { get; set; }
        public bool IsJsonType { get; set; }
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new();
        public bool IsIdIsWritable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public int PkOrder { get; set; }
        public bool IsVersion { get; set; }
        public bool IsCreatedBy { get; set; }
        public bool IsCreatedOn { get; set; }
        public bool IsLastUpdatedBy { get; set; }
        public bool IsLastUpdatedOn { get; set; }
        public int Ordinal { get; set; }
        public object? MakeParameterValueFromField<T>(T objectToCreate) => null;
    }

    private static void InvokeValidateVersionType(Type entityType, IColumnInfo? column)
    {
        var method = typeof(TypeMapRegistry).GetMethod("ValidateVersionType", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, new object?[] { entityType, column });
    }

    [Fact]
    public void EmptyTableName_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<EmptyTableNameEntity>());
    }

    [Fact]
    public void EmptyColumnName_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<EmptyColumnNameEntity>());
    }

    [Fact]
    public void EnumColumn_InvalidDbType_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<EnumInvalidDbTypeEntity>());
    }

    [Fact]
    public void JsonVersionColumn_DisablesJsonFlag()
    {
        var registry = new TypeMapRegistry();
        var table = registry.GetTableInfo<JsonVersionEntity>();

        Assert.NotNull(table.Version);
        Assert.False(table.Version!.IsJsonType);
        Assert.True(table.Version.IsVersion);
    }

    [Fact]
    public void PrimaryKeyGap_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<PrimaryKeyGapEntity>());
    }

    [Fact]
    public void PrimaryKeyDuplicate_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<PrimaryKeyDuplicateEntity>());
    }

    [Fact]
    public void NegativeOrdinal_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<NegativeOrdinalEntity>());
    }

    [Fact]
    public void DuplicateOrdinal_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<DuplicateOrdinalEntity>());
    }

    [Fact]
    public void InvalidLastUpdatedOn_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<InvalidLastUpdatedOnEntity>());
    }

    [Fact]
    public void InvalidCreatedBy_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<InvalidCreatedByEntity>());
    }

    [Fact]
    public void JsonInference_SetsJsonType()
    {
        var registry = new TypeMapRegistry();
        var table = registry.GetTableInfo<JsonInferenceEntity>();

        var payload = table.Columns["payload"];
        var node = table.Columns["node"];

        Assert.True(payload.IsJsonType);
        Assert.True(node.IsJsonType);
    }

    [Fact]
    public void InvalidVersionType_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<InvalidVersionTypeEntity>());
    }

    [Fact]
    public void OrdinalAssignment_FillsMissingSlots()
    {
        var registry = new TypeMapRegistry();
        var table = registry.GetTableInfo<OrdinalAssignmentEntity>();

        Assert.Equal(2, table.Columns["a"].Ordinal);
        Assert.Equal(1, table.Columns["b"].Ordinal);
    }

    [Fact]
    public void ValidateVersionType_ThrowsForUnsupportedType()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = typeof(InvalidVersionTypeEntity).GetProperty(nameof(InvalidVersionTypeEntity.Version))!
        };

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokeValidateVersionType(typeof(InvalidVersionTypeEntity), column));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }
}
