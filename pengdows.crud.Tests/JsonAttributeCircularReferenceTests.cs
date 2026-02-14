// =============================================================================
// FILE: JsonAttributeCircularReferenceTests.cs
// PURPOSE: Tests for JsonAttribute behavior with circular references.
//          Documents current limitations and expected behavior.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class JsonAttributeCircularReferenceTests
{
    [Table("test_json")]
    public class EntityWithJson
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [attributes.Json]
        [Column("data", DbType.String)]
        public CircularTestData? Data { get; set; }
    }

    public class CircularTestData
    {
        public string Name { get; set; } = "";
        public CircularTestData? Parent { get; set; }
        public List<CircularTestData> Children { get; set; } = new();
    }

    [Fact]
    public void JsonAttribute_WithCircularReference_ThrowsJsonException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        // Create circular reference
        var data = new CircularTestData { Name = "Parent" };
        var child = new CircularTestData { Name = "Child", Parent = data };
        data.Children.Add(child);

        var entity = new EntityWithJson { Data = data };
        var helper = new TableGateway<EntityWithJson, int>(context);

        // Act & Assert
        // This documents that circular references will cause JsonException
        // Users must handle this in their data structures or use custom serialization
        var ex = Assert.Throws<JsonException>(() =>
        {
            var container = helper.BuildCreate(entity, context);
        });

        // The exception message should mention cycle detection
        Assert.True(ex.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JsonAttribute_NullValue_DoesNotThrow()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        var entity = new EntityWithJson { Data = null };
        var helper = new TableGateway<EntityWithJson, int>(context);

        // Act
        var container = helper.BuildCreate(entity, context);

        // Assert - Should not throw, null is serialized correctly
        Assert.NotNull(container);
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void JsonAttribute_SimpleObject_SerializesCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        var data = new CircularTestData { Name = "Simple" };
        var entity = new EntityWithJson { Data = data };
        var helper = new TableGateway<EntityWithJson, int>(context);

        // Act
        var container = helper.BuildCreate(entity, context);

        // Assert - Simple objects without circular references work fine
        Assert.NotNull(container);
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void TypeMapRegistry_BuildsDefaultJsonOptions()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();

        // Act
        var tableInfo = typeMap.GetTableInfo<EntityWithJson>();
        var dataColumn = tableInfo.Columns["data"];

        // Assert
        // Verify that JsonSerializerOptions are created (not null)
        Assert.NotNull(dataColumn.JsonSerializerOptions);
        // Verify PropertyNameCaseInsensitive is set to true by default
        Assert.True(dataColumn.JsonSerializerOptions.PropertyNameCaseInsensitive);
    }
}
