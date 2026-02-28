using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests verifying the behavioral contract preserved by the 6 performance optimizations:
/// 1. ID column getter in Update.cs    (raw PropertyInfo.GetValue → FastGetter)
/// 2. Audit CreatedOn/CreatedBy getter (raw PropertyInfo.GetValue → FastGetter)
/// 3. Cached audit setters             (GetOrCreateSetter per call → instance fields)
/// 4. Version column getter            (raw PropertyInfo.GetValue → FastGetter)
/// 5. Writable ID getter               (raw PropertyInfo.GetValue → FastGetter)
/// 6. AddParameters without redundant  .OfType cast
/// </summary>
[Collection("SqliteSerial")]
public class PropertyAccessOptimizationTests : SqlLiteContextTestBase
{
    // ============================================================================
    // ENTITIES
    // ============================================================================

    [Table("opt_simple")]
    private class SimpleEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [PrimaryKey(1)]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    [Table("opt_versioned")]
    private class VersionedEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [PrimaryKey(1)]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int? Version { get; set; }
    }

    [Table("opt_time_audit")]
    private class TimeAuditEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("created_on", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [LastUpdatedOn]
        [Column("last_updated_on", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    [Table("opt_user_audit")]
    private class UserAuditEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedBy]
        [Column("created_by", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [CreatedOn]
        [Column("created_on", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("last_updated_on", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    [Table("opt_writable_id")]
    private class WritableGuidIdEntity
    {
        [Id] [Column("id", DbType.Guid)] public Guid Id { get; set; }

        [PrimaryKey(1)]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    public PropertyAccessOptimizationTests()
    {
        TypeMap.Register<SimpleEntity>();
        TypeMap.Register<VersionedEntity>();
        TypeMap.Register<TimeAuditEntity>();
        TypeMap.Register<UserAuditEntity>();
        TypeMap.Register<WritableGuidIdEntity>();
    }

    // ============================================================================
    // GROUP 1: ID Column Getter (Update.cs:106, 135)
    // The entity's ID value must be correctly read for the WHERE clause.
    // ============================================================================

    [Fact]
    public async Task BuildUpdateAsync_WithNonDefaultId_GeneratesUpdateSqlWithWhereClause()
    {
        // Arrange
        var entity = new SimpleEntity { Id = 99, Name = "test", Value = 1 };
        var helper = new TableGateway<SimpleEntity, long>(Context);

        // Act
        var sc = await helper.BuildUpdateAsync(entity, false);
        var sql = sc.Query.ToString();

        // Assert - must generate UPDATE ... WHERE (id getter must succeed)
        Assert.NotEmpty(sql);
        Assert.Contains("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildUpdateAsync_WithNonDefaultId_IncludesParametersForColumnsAndId()
    {
        // Arrange - entity with non-default ID ensures the getter must return a meaningful value
        var entity = new SimpleEntity { Id = 42, Name = "hello", Value = 7 };
        var helper = new TableGateway<SimpleEntity, long>(Context);

        // Act
        var sc = await helper.BuildUpdateAsync(entity, false);

        // Assert - SET(name) + SET(value) + WHERE(id) = 3 parameters
        Assert.Equal(3, sc.ParameterCount);
    }

    // ============================================================================
    // GROUP 2: Audit CreatedOn/CreatedBy getter (Audit.cs:138, 149)
    // Pre-existing audit values must NOT be overwritten on Create.
    // ============================================================================

    [Fact]
    public void BuildCreate_WhenCreatedOnAlreadySet_PreservesExistingValue()
    {
        // Arrange
        var preExisting = new DateTime(2020, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var entity = new TimeAuditEntity { Name = "test", CreatedOn = preExisting };
        var helper = new TableGateway<TimeAuditEntity, int>(Context, AuditValueResolver);

        // Act - BuildCreate → MutateEntityForInsert → SetAuditFields(updateOnly: false)
        // Audit.cs:138 reads current CreatedOn to decide whether to overwrite
        helper.BuildCreate(entity);

        // Assert - CreatedOn must NOT be overwritten
        Assert.Equal(preExisting, entity.CreatedOn);
    }

    [Fact]
    public void BuildCreate_WhenCreatedOnIsDefault_SetsCreatedOn()
    {
        // Arrange - unset CreatedOn (default DateTime) must be populated
        var entity = new TimeAuditEntity { Name = "test", CreatedOn = default };
        var helper = new TableGateway<TimeAuditEntity, int>(Context, AuditValueResolver);

        // Act
        helper.BuildCreate(entity);

        // Assert
        Assert.NotEqual(default, entity.CreatedOn);
    }

    [Fact]
    public void BuildCreate_WhenCreatedByAlreadySet_PreservesExistingValue()
    {
        // Arrange
        var entity = new UserAuditEntity { Name = "test", CreatedBy = "original-user" };
        var helper = new TableGateway<UserAuditEntity, int>(Context, AuditValueResolver);

        // Act - Audit.cs:149 reads current CreatedBy before deciding to overwrite
        helper.BuildCreate(entity);

        // Assert - CreatedBy must NOT be overwritten
        Assert.Equal("original-user", entity.CreatedBy);
    }

    [Fact]
    public void BuildCreate_WhenCreatedByIsEmpty_SetsCreatedByFromResolver()
    {
        // Arrange - empty CreatedBy must be populated by AuditValueResolver
        var entity = new UserAuditEntity { Name = "test", CreatedBy = string.Empty };
        var helper = new TableGateway<UserAuditEntity, int>(Context, AuditValueResolver);

        // Act
        helper.BuildCreate(entity);

        // Assert - resolver provides "test-user" (from StubAuditValueResolver("test-user"))
        Assert.Equal("test-user", entity.CreatedBy);
    }

    // ============================================================================
    // GROUP 3: Cached Audit Setters (Audit.cs:117, 125, 141, 156)
    // GetOrCreateSetter is called per-operation; must produce correct results
    // whether called once or many times in sequence.
    // ============================================================================

    [Fact]
    public void BuildCreate_SetsAllFourAuditFields()
    {
        // Arrange
        var entity = new UserAuditEntity { Name = "new-entity" };
        var helper = new TableGateway<UserAuditEntity, int>(Context, AuditValueResolver);

        // Act
        helper.BuildCreate(entity);

        // Assert - all 4 audit setters must fire correctly
        Assert.NotEqual(default, entity.CreatedOn);
        Assert.NotEqual(default, entity.LastUpdatedOn);
        Assert.NotEmpty(entity.CreatedBy);
        Assert.NotEmpty(entity.LastUpdatedBy);
    }

    [Fact]
    public async Task BuildUpdateAsync_SetsOnlyLastUpdatedFields_NotCreatedFields()
    {
        // Arrange - entity with known CreatedOn/CreatedBy
        var originalCreatedOn = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity = new UserAuditEntity
        {
            Id = 1,
            Name = "existing",
            CreatedOn = originalCreatedOn,
            CreatedBy = "creator"
        };
        var helper = new TableGateway<UserAuditEntity, int>(Context, AuditValueResolver);

        // Act - update path calls SetAuditFields(updateOnly: true)
        // Only LastUpdated setters fire; Created setters must NOT fire
        await helper.BuildUpdateAsync(entity, false);

        // Assert - LastUpdated populated; Created unchanged
        Assert.NotEqual(default, entity.LastUpdatedOn);
        Assert.NotEmpty(entity.LastUpdatedBy);
        Assert.Equal(originalCreatedOn, entity.CreatedOn); // must be preserved
        Assert.Equal("creator", entity.CreatedBy); // must be preserved
    }

    [Fact]
    public void BuildCreate_CalledMultipleTimes_SetsAuditFieldsOnEachEntity()
    {
        // Arrange - verifies no shared-state corruption from cached setter fields
        var helper = new TableGateway<TimeAuditEntity, int>(Context, AuditValueResolver);

        // Act - create 3 separate entities sequentially
        var e1 = new TimeAuditEntity { Name = "a" };
        var e2 = new TimeAuditEntity { Name = "b" };
        var e3 = new TimeAuditEntity { Name = "c" };
        helper.BuildCreate(e1);
        helper.BuildCreate(e2);
        helper.BuildCreate(e3);

        // Assert - all entities have audit fields (not just the first, which would indicate
        // a caching bug where the setter is only stored for the first entity)
        Assert.NotEqual(default, e1.CreatedOn);
        Assert.NotEqual(default, e2.CreatedOn);
        Assert.NotEqual(default, e3.CreatedOn);
        Assert.NotEqual(default, e1.LastUpdatedOn);
        Assert.NotEqual(default, e2.LastUpdatedOn);
        Assert.NotEqual(default, e3.LastUpdatedOn);
    }

    // ============================================================================
    // GROUP 4: Version Column Getter (Core.cs:655, Upsert.cs:119)
    // Version must be read correctly and initialized to 1 when null/zero.
    // ============================================================================

    [Fact]
    public void BuildCreate_WithVersionNull_InitializesVersionToOne()
    {
        // Arrange
        var entity = new VersionedEntity { Name = "test", Version = null };
        var helper = new TableGateway<VersionedEntity, long>(Context);

        // Act - MutateEntityForInsert reads version via PropertyInfo.GetValue (Core.cs:655)
        helper.BuildCreate(entity);

        // Assert - version must be initialized to 1
        Assert.Equal(1, entity.Version);
    }

    [Fact]
    public void BuildCreate_WithVersionZero_InitializesVersionToOne()
    {
        // Arrange
        var entity = new VersionedEntity { Name = "test", Version = 0 };
        var helper = new TableGateway<VersionedEntity, long>(Context);

        // Act
        helper.BuildCreate(entity);

        // Assert
        Assert.Equal(1, entity.Version);
    }

    [Fact]
    public void BuildCreate_WithVersionAlreadySet_PreservesVersion()
    {
        // Arrange - version > 0 must NOT be reset
        var entity = new VersionedEntity { Name = "test", Version = 5 };
        var helper = new TableGateway<VersionedEntity, long>(Context);

        // Act
        helper.BuildCreate(entity);

        // Assert
        Assert.Equal(5, entity.Version);
    }

    [Fact]
    public void BuildUpsert_WithVersionNull_InitializesVersionToOne()
    {
        // Arrange - upsert path reads version via PropertyInfo.GetValue (Upsert.cs:119)
        var entity = new VersionedEntity { Name = "test", Version = null };
        var helper = new TableGateway<VersionedEntity, long>(Context);

        // Act - BuildUpsert → BuildUpsertOnConflict → PrepareForInsertOrUpsert
        var sc = helper.BuildUpsert(entity);

        // Assert - version was initialized to 1 by PrepareForInsertOrUpsert
        Assert.NotNull(sc);
        Assert.Equal(1, entity.Version);
    }

    // ============================================================================
    // GROUP 5: Writable ID Getter / EnsureWritableIdHasValue (Core.cs:742)
    // Writable Guid IDs must be auto-generated when empty.
    // ============================================================================

    [Fact]
    public void BuildCreate_WithWritableGuidId_WhenEmpty_GeneratesNewGuid()
    {
        // Arrange - Id = Guid.Empty; EnsureWritableIdHasValue must auto-generate one
        var entity = new WritableGuidIdEntity { Name = "test" };
        var helper = new TableGateway<WritableGuidIdEntity, Guid>(Context);

        // Act - Core.cs:742 reads current Guid via PropertyInfo.GetValue
        helper.BuildCreate(entity);

        // Assert - Id must now be a non-empty Guid
        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public void BuildCreate_WithWritableGuidId_WhenAlreadySet_PreservesId()
    {
        // Arrange - existing non-empty Guid must be preserved
        var existingId = Guid.NewGuid();
        var entity = new WritableGuidIdEntity { Id = existingId, Name = "test" };
        var helper = new TableGateway<WritableGuidIdEntity, Guid>(Context);

        // Act
        helper.BuildCreate(entity);

        // Assert
        Assert.Equal(existingId, entity.Id);
    }

    // ============================================================================
    // GROUP 6: AddParameters Without Redundant .OfType (SqlContainer.cs:1297)
    // All DbParameter items must be added from any IEnumerable<DbParameter> source.
    // ============================================================================

    [Fact]
    public void AddParameters_WithArray_AddsAllParameters()
    {
        // Arrange
        var sc = Context.CreateSqlContainer("SELECT 1");
        var p1 = sc.CreateDbParameter("p1", DbType.String, "value1");
        var p2 = sc.CreateDbParameter("p2", DbType.Int32, 42);
        var p3 = sc.CreateDbParameter("p3", DbType.Boolean, true);

        // Act
        sc.AddParameters(new DbParameter[] { p1, p2, p3 });

        // Assert
        Assert.Equal(3, sc.ParameterCount);
    }

    [Fact]
    public void AddParameters_WithList_AddsAllParameters()
    {
        // Arrange
        var sc = Context.CreateSqlContainer("SELECT 1");
        var p1 = sc.CreateDbParameter("a", DbType.Int64, 1L);
        var p2 = sc.CreateDbParameter("b", DbType.Int64, 2L);

        // Act
        sc.AddParameters(new List<DbParameter> { p1, p2 });

        // Assert
        Assert.Equal(2, sc.ParameterCount);
    }

    [Fact]
    public void AddParameters_WithEmptyCollection_AddsNoParameters()
    {
        // Arrange
        var sc = Context.CreateSqlContainer("SELECT 1");

        // Act
        sc.AddParameters(Array.Empty<DbParameter>());

        // Assert
        Assert.Equal(0, sc.ParameterCount);
    }

    [Fact]
    public void AddParameters_WithNull_DoesNotThrow()
    {
        // Arrange
        var sc = Context.CreateSqlContainer("SELECT 1");

        // Act & Assert - null guard must remain after removing .OfType
        var ex = Record.Exception(() => sc.AddParameters(null!));
        Assert.Null(ex);
        Assert.Equal(0, sc.ParameterCount);
    }

    [Fact]
    public void AddParameters_WithYieldedEnumerable_AddsAllParameters()
    {
        // Arrange - lazy enumerable (common real-world pattern)
        var sc = Context.CreateSqlContainer("SELECT 1");
        var p1 = sc.CreateDbParameter("x", DbType.String, "hello");
        var p2 = sc.CreateDbParameter("y", DbType.String, "world");

        static IEnumerable<DbParameter> Yield(DbParameter a, DbParameter b)
        {
            yield return a;
            yield return b;
        }

        // Act
        sc.AddParameters(Yield(p1, p2));

        // Assert
        Assert.Equal(2, sc.ParameterCount);
    }
}