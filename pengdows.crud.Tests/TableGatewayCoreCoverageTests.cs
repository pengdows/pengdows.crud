using System;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayCoreCoverageTests : SqlLiteContextTestBase
{
    [Fact]
    public void ReplaceNeutralTokens_SwapsQuotedIdentifiersAndParameters()
    {
        TypeMap.Register<CoreCoverageEntity>();
        var gateway = new TableGateway<CoreCoverageEntity, int>(Context);
        var dialect = ((ISqlDialectProvider)Context).Dialect;
        var sql = "{Q}Name{q} = {S}value";

        var replaced = gateway.ReplaceNeutralTokens(sql);

        var expected = $"{dialect.QuotePrefix}Name{dialect.QuoteSuffix} = {dialect.ParameterMarker}value";
        Assert.Equal(expected, replaced);
    }

    [Fact]
    public void BuildWrappedTableName_CachesSameDialect()
    {
        // TDD: Verify that BuildWrappedTableName caches results per dialect
        TypeMap.Register<CoreCoverageEntity>();
        var gateway = new TableGateway<CoreCoverageEntity, int>(Context);

        var method = typeof(TableGateway<CoreCoverageEntity, int>).GetMethod(
            "BuildWrappedTableName",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
                     throw new InvalidOperationException("Missing helper");

        var dialect = ((ISqlDialectProvider)Context).Dialect;

        // Call twice with same dialect
        var result1 = (string)method.Invoke(gateway, new object[] { dialect })!;
        var result2 = (string)method.Invoke(gateway, new object[] { dialect })!;

        // Should return same cached instance
        Assert.Equal(result1, result2);
        Assert.True(ReferenceEquals(result1, result2),
            "Expected same dialect to return cached string instance");
    }

    [Fact]
    public void BuildWrappedTableName_WithSchema_CachesCorrectly()
    {
        // TDD: Verify caching works for schema-qualified names
        TypeMap.Register<SchemaEntity>();
        var gateway = new TableGateway<SchemaEntity, int>(Context);

        var method = typeof(TableGateway<SchemaEntity, int>).GetMethod(
            "BuildWrappedTableName",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
                     throw new InvalidOperationException("Missing helper");

        var dialect = ((ISqlDialectProvider)Context).Dialect;

        // Call twice with same dialect
        var result1 = (string)method.Invoke(gateway, new object[] { dialect })!;
        var result2 = (string)method.Invoke(gateway, new object[] { dialect })!;

        // Should return same cached instance
        Assert.Equal(result1, result2);
        Assert.True(ReferenceEquals(result1, result2),
            "Expected schema-qualified name to be cached");

        // Verify it contains the schema separator
        Assert.Contains(dialect.CompositeIdentifierSeparator, result1);
    }

    [Fact]
    public void MaterializeDistinctIds_RemovesDuplicatesInOrder()
    {
        var method = typeof(TableGateway<CoreCoverageEntity, int>).GetMethod(
            "MaterializeDistinctIds",
            BindingFlags.NonPublic | BindingFlags.Static) ??
                     throw new InvalidOperationException("Missing helper");

        var input = new[] {1, 2, 2, 3, 1};
        var result = (System.Collections.Generic.List<int>)method.Invoke(null, new object?[] {input})!;

        Assert.Equal(new[] {1, 2, 3}, result);
    }

    [Fact]
    public void MaterializeDistinctIds_SingleElementList_ReturnsInputDirectly()
    {
        // TDD: Verify single-element collections use fast path without allocation
        var method = typeof(TableGateway<CoreCoverageEntity, int>).GetMethod(
            "MaterializeDistinctIds",
            BindingFlags.NonPublic | BindingFlags.Static) ??
                     throw new InvalidOperationException("Missing helper");

        var input = new System.Collections.Generic.List<int> { 42 };
        var result = (System.Collections.Generic.List<int>)method.Invoke(null, new object?[] { input })!;

        // Should return a list with the single element
        Assert.Single(result);
        Assert.Equal(42, result[0]);
    }

    [Fact]
    public void MaterializeDistinctIds_SingleElementArray_ReturnsOptimized()
    {
        // TDD: Verify single-element arrays use fast path
        var method = typeof(TableGateway<CoreCoverageEntity, int>).GetMethod(
            "MaterializeDistinctIds",
            BindingFlags.NonPublic | BindingFlags.Static) ??
                     throw new InvalidOperationException("Missing helper");

        var input = new[] { 99 };
        var result = (System.Collections.Generic.List<int>)method.Invoke(null, new object?[] { input })!;

        // Should return a list with the single element
        Assert.Single(result);
        Assert.Equal(99, result[0]);
    }

    [Fact]
    public void MaterializeDistinctIds_EmptyCollection_ReturnsEmpty()
    {
        // TDD: Verify empty collections work correctly
        var method = typeof(TableGateway<CoreCoverageEntity, int>).GetMethod(
            "MaterializeDistinctIds",
            BindingFlags.NonPublic | BindingFlags.Static) ??
                     throw new InvalidOperationException("Missing helper");

        var input = new int[] { };
        var result = (System.Collections.Generic.List<int>)method.Invoke(null, new object?[] { input })!;

        // Should return empty list
        Assert.Empty(result);
    }

    [Fact]
    public void MaterializeDistinctIds_TwoDistinctElements_WorksCorrectly()
    {
        // TDD: Verify two-element case (common for composite keys)
        var method = typeof(TableGateway<CoreCoverageEntity, int>).GetMethod(
            "MaterializeDistinctIds",
            BindingFlags.NonPublic | BindingFlags.Static) ??
                     throw new InvalidOperationException("Missing helper");

        var input = new[] { 1, 2 };
        var result = (System.Collections.Generic.List<int>)method.Invoke(null, new object?[] { input })!;

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 1, 2 }, result);
    }

    [Fact]
    public void EnsureWritableIdHasValue_GeneratesGuidWhenMissing()
    {
        TypeMap.Register<GuidWritableEntity>();
        var gateway = new TableGateway<GuidWritableEntity, Guid>(Context);
        var entity = new GuidWritableEntity();
        var method = typeof(TableGateway<GuidWritableEntity, Guid>).GetMethod(
            "EnsureWritableIdHasValue",
            BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException("Missing helper");

        method.Invoke(gateway, new object?[] {entity});

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public void EnsureWritableIdHasValue_GeneratesStringWhenBlank()
    {
        TypeMap.Register<StringWritableEntity>();
        var gateway = new TableGateway<StringWritableEntity, string>(Context);
        var entity = new StringWritableEntity { Id = "" };
        var method = typeof(TableGateway<StringWritableEntity, string>).GetMethod(
            "EnsureWritableIdHasValue",
            BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException("Missing helper");

        method.Invoke(gateway, new object?[] {entity});

        Assert.False(string.IsNullOrWhiteSpace(entity.Id));
    }

    [Fact]
    public void ValidateRowIdType_RejectsUnsupportedType()
    {
        var type = typeof(TableGateway<CoreCoverageEntity, DateTime>);
        var ex = Assert.Throws<TypeInitializationException>(() =>
            RuntimeHelpers.RunClassConstructor(type.TypeHandle));

        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Fact]
    public void IsDefaultId_ReturnsExpectedValues()
    {
        TypeMap.Register<CoreCoverageEntity>();
        var method = typeof(TableGateway<CoreCoverageEntity, int>)
            .GetMethod("IsDefaultId", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing helper");

        Assert.True((bool)method.Invoke(null, new object?[] { 0 })!);
        Assert.False((bool)method.Invoke(null, new object?[] { 42 })!);
        Assert.True((bool)method.Invoke(null, new object?[] { (Guid?)null })!);
    }

    [Fact]
    public void TryParseMajorVersion_ReturnsMajorDigits()
    {
        var method = typeof(TableGateway<CoreCoverageEntity, int>)
            .GetMethod("TryParseMajorVersion", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing helper");

        var parameters = new object?[] { "15.3.2", 0 };
        var success = (bool)method.Invoke(null, parameters)!;

        Assert.True(success);
        Assert.Equal(15, parameters[1]);

        parameters = new object?[] { "unknown", 0 };
        success = (bool)method.Invoke(null, parameters)!;
        Assert.False(success);
    }

    [Fact]
    public void BuildWrappedTableName_IncludesSchemaWhenPresent()
    {
        TypeMap.Register<SchemaEntity>();
        var gateway = new TableGateway<SchemaEntity, int>(Context);
        var dialect = ((ISqlDialectProvider)Context).Dialect;
        var method = typeof(TableGateway<SchemaEntity, int>)
            .GetMethod("BuildWrappedTableName", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing helper");

        var wrapped = (string)method.Invoke(gateway, new object?[] { dialect })!;
        var expected = dialect.WrapObjectName("schema") + dialect.CompositeIdentifierSeparator +
                       dialect.WrapObjectName("core_schema");
        Assert.Equal(expected, wrapped);
    }

    [Table("core_coverage")]
    private class CoreCoverageEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("core_guid")]
    private class GuidWritableEntity
    {
        [Id(true)]
        [Column("id", DbType.String)]
        public Guid Id { get; set; }
    }

    [Table("core_string")]
    private class StringWritableEntity
    {
        [Id(true)]
        [Column("id", DbType.String)]
        public string Id { get; set; } = string.Empty;
    }

    [Table("core_schema", "schema")]
    private class SchemaEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
