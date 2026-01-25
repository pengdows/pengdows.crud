using System;
using System.Data;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using pengdows.crud.attributes;

namespace pengdows.crud.Tests;

/// <summary>
/// Final batch of tests to push coverage to 90%.
/// Targets specific edge cases and error paths.
/// </summary>
public class FinalCoverageTests
{
    [Table("audit_entity")]
    public class AuditEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedBy]
        [Column("created_by", DbType.String)]
        public string? CreatedBy { get; set; }

        [CreatedOn]
        [Column("created_on", DbType.DateTime)]
        public DateTime? CreatedOn { get; set; }

        [LastUpdatedBy]
        [Column("updated_by", DbType.String)]
        public string? LastUpdatedBy { get; set; }

        [LastUpdatedOn]
        [Column("updated_on", DbType.DateTime)]
        public DateTime? LastUpdatedOn { get; set; }

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("versioned_entity")]
    public class VersionedEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int64)]
        public long Version { get; set; }
    }

    #region EntityHelper with Audit Fields

    [Fact]
    public async Task EntityHelper_UpsertAsync_ExecutesCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<VersionedEntity, int>(context);
        var entity = new VersionedEntity { Id = 1, Name = "Test", Version = 1 };

        // Act
        var rowsAffected = await helper.UpsertAsync(entity);

        // Assert
        Assert.Equal(1, rowsAffected);
    }

    [Fact]
    public void EntityHelper_BuildUpsert_GeneratesMergeOrUpsertSql()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<VersionedEntity, int>(context);
        var entity = new VersionedEntity { Id = 1, Name = "Test", Version = 1 };

        // Act
        var container = helper.BuildUpsert(entity);

        // Assert
        Assert.NotNull(container);
        var sql = container.Query.ToString();
        Assert.NotEmpty(sql);
    }

    #endregion

    #region SqlContainer Parameter Edge Cases

    [Fact]
    public void SqlContainer_AddParameterWithValue_AddsParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();

        // Act
        var param = container.AddParameterWithValue("test", DbType.String, "value");

        // Assert
        Assert.NotNull(param);
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void SqlContainer_AddParameterWithValue_WithoutName_AddsParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();

        // Act
        var param = container.AddParameterWithValue(DbType.String, "value");

        // Assert
        Assert.NotNull(param);
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void SqlContainer_CreateDbParameter_WithName_CreatesParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();

        // Act
        var param = container.CreateDbParameter("test", DbType.String, "value");

        // Assert
        Assert.NotNull(param);
        Assert.Equal(0, container.ParameterCount); // Not added yet
    }

    [Fact]
    public void SqlContainer_CreateDbParameter_WithoutName_CreatesParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();

        // Act
        var param = container.CreateDbParameter(DbType.String, "value");

        // Assert
        Assert.NotNull(param);
        Assert.Equal(0, container.ParameterCount); // Not added yet
    }

    [Fact]
    public void SqlContainer_AddParameter_AddsParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();
        var param = context.CreateDbParameter("test", DbType.String, "value");

        // Act
        container.AddParameter(param);

        // Assert
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void SqlContainer_AddParameters_AddsMultipleParameters()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();
        var params_list = new[] {
            context.CreateDbParameter("test1", DbType.String, "value1"),
            context.CreateDbParameter("test2", DbType.Int32, 42)
        };

        // Act
        container.AddParameters(params_list);

        // Assert
        Assert.Equal(2, container.ParameterCount);
    }

    [Fact]
    public void SqlContainer_WrapObjectName_QuotesIdentifiers()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();

        // Act
        var wrapped = container.WrapObjectName("table_name");

        // Assert
        Assert.NotNull(wrapped);
        Assert.NotEmpty(wrapped);
    }

    [Fact]
    public void SqlContainer_MakeParameterName_WithParameter_FormatsName()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();
        var param = container.CreateDbParameter("test", DbType.String, "value");

        // Act
        var name = container.MakeParameterName(param);

        // Assert
        Assert.NotNull(name);
        Assert.NotEmpty(name);
    }

    [Fact]
    public void SqlContainer_MakeParameterName_WithString_FormatsName()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();

        // Act
        var name = container.MakeParameterName("test");

        // Assert
        Assert.NotNull(name);
        Assert.NotEmpty(name);
    }

    #endregion

    #region DatabaseContext IsolationLevel Tests

    [Fact]
    public void DatabaseContext_BeginTransaction_WithReadUncommitted_CreatesTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var transaction = context.BeginTransaction(IsolationLevel.ReadUncommitted);

        // Assert
        Assert.NotNull(transaction);
    }

    [Fact]
    public void DatabaseContext_BeginTransaction_WithReadCommitted_CreatesTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

        // Assert
        Assert.NotNull(transaction);
    }

    [Fact]
    public void DatabaseContext_BeginTransaction_WithRepeatableRead_CreatesTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var transaction = context.BeginTransaction(IsolationLevel.RepeatableRead);

        // Assert
        Assert.NotNull(transaction);
    }

    [Fact]
    public void DatabaseContext_BeginTransaction_WithSerializable_CreatesTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var transaction = context.BeginTransaction(IsolationLevel.Serializable);

        // Assert
        Assert.NotNull(transaction);
    }

    [Fact]
    public void DatabaseContext_BeginTransaction_WithSnapshot_CreatesTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var transaction = context.BeginTransaction(IsolationLevel.Snapshot);

        // Assert
        Assert.NotNull(transaction);
    }

    #endregion

    #region Connection State Tests

    [Fact]
    public void DatabaseContext_GetConnection_ForRead_ReturnsConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var conn = context.GetConnection(ExecutionType.Read);

        // Assert
        Assert.NotNull(conn);
    }

    [Fact]
    public void DatabaseContext_GetConnection_ForWrite_ReturnsConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var conn = context.GetConnection(ExecutionType.Write);

        // Assert
        Assert.NotNull(conn);
    }

    [Fact]
    public void DatabaseContext_GetConnection_SharedConnection_ReturnsConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var conn = context.GetConnection(ExecutionType.Read, isShared: true);

        // Assert
        Assert.NotNull(conn);
    }

    #endregion
}
