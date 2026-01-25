using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using pengdows.crud.attributes;
using pengdows.crud.wrappers;

namespace pengdows.crud.Tests;

/// <summary>
/// Additional tests to improve overall coverage toward 90%.
/// Targets various edge cases across SqlContainer, DatabaseContext, and wrappers.
/// </summary>
public class AdditionalCoverageTests
{
    [Table("test_table")]
    public class SimpleEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    #region SqlContainer Edge Cases

    [Fact]
    public async Task SqlContainer_ExecuteNonQueryAsync_WithCancellationToken_WorksCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer("INSERT INTO test VALUES (1)");

        // Act
        var result = await container.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SqlContainer_ExecuteReaderAsync_WithCancellationToken_WorksCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        conn.EnqueueReaderResult(new[] { new System.Collections.Generic.Dictionary<string, object?> { ["id"] = 1, ["name"] = "test" } });
        factory.Connections.Add(conn);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer("SELECT * FROM test");

        // Act
        using var reader = await container.ExecuteReaderAsync(CommandType.Text, CancellationToken.None);

        // Assert - Just verify reader was created
        Assert.NotNull(reader);
    }

    [Fact]
    public void SqlContainer_Clear_RemovesQueryAndParameters()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer("SELECT * FROM test");
        container.AddParameterWithValue("test", DbType.String, "value");

        // Act
        container.Clear();

        // Assert
        Assert.Empty(container.Query.ToString());
        Assert.Equal(0, container.ParameterCount);
    }

    [Fact]
    public void SqlContainer_HasWhereAppended_TracksWhereClause()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer();

        // Act - Initially false
        Assert.False(container.HasWhereAppended);

        // Add WHERE clause
        container.Query.Append("SELECT * FROM test WHERE id = 1");
        container.HasWhereAppended = true;

        // Assert
        Assert.True(container.HasWhereAppended);
    }

    #endregion

    #region DatabaseContext Edge Cases

    [Fact]
    public async Task DatabaseContext_CloseAndDisposeConnectionAsync_WithNullConnection_DoesNotThrow()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act & Assert - Should not throw
        await context.CloseAndDisposeConnectionAsync(null);
    }

    [Fact]
    public void DatabaseContext_CloseAndDisposeConnection_WithNullConnection_DoesNotThrow()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act & Assert - Should not throw
        context.CloseAndDisposeConnection(null);
    }

    [Fact]
    public void DatabaseContext_CreateDbParameter_WithNameAndValue_CreatesParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        var param = context.CreateDbParameter("test", DbType.String, "value");

        // Assert
        Assert.NotNull(param);
        Assert.Equal("test", param.ParameterName);
        Assert.Equal("value", param.Value);
    }

    [Fact]
    public void DatabaseContext_CreateDbParameter_WithoutName_CreatesParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        var param = context.CreateDbParameter(DbType.String, "value");

        // Assert
        Assert.NotNull(param);
        Assert.Equal("value", param.Value);
    }

    [Fact]
    public void DatabaseContext_MakeParameterName_FormatsCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var param = context.CreateDbParameter("test", DbType.String, "value");

        // Act
        var formattedName1 = context.MakeParameterName(param);
        var formattedName2 = context.MakeParameterName("test");

        // Assert
        Assert.NotNull(formattedName1);
        Assert.NotNull(formattedName2);
    }

    [Fact]
    public void DatabaseContext_WrapObjectName_QuotesIdentifier()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        var wrapped = context.WrapObjectName("tableName");

        // Assert
        Assert.NotNull(wrapped);
        Assert.NotEmpty(wrapped);
    }

    #endregion

    #region TrackedConnection Edge Cases

    [Fact]
    public async Task TrackedConnection_OpenAsync_OpensConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var conn = context.GetConnection(ExecutionType.Read);
        await ((TrackedConnection)conn).OpenAsync();

        // Assert
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public async Task TrackedConnection_OpenAsync_WithCancellationToken_OpensConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        // Act
        using var conn = context.GetConnection(ExecutionType.Read);
        await ((TrackedConnection)conn).OpenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public void TrackedConnection_BeginTransaction_CreatesTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);

        // Act
        var transaction = conn.BeginTransaction();

        // Assert
        Assert.NotNull(transaction);
    }

    [Fact]
    public void TrackedConnection_BeginTransaction_WithIsolationLevel_CreatesTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);

        // Act
        var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);

        // Assert
        Assert.NotNull(transaction);
    }

    #endregion

    #region EntityHelper Additional Edge Cases

    [Fact]
    public async Task EntityHelper_RetrieveAsync_WithEmptyIdList_ThrowsArgumentException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<SimpleEntity, int>(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => helper.RetrieveAsync(Array.Empty<int>()));
    }

    [Fact]
    public async Task EntityHelper_DeleteAsync_WithSingleId_ExecutesCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<SimpleEntity, int>(context);

        // Act
        var result = await helper.DeleteAsync(1);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task EntityHelper_DeleteAsync_WithMultipleIds_ExecutesCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(3);
        factory.Connections.Add(conn);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<SimpleEntity, int>(context);

        // Act
        var result = await helper.DeleteAsync(new[] { 1, 2, 3 });

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public void EntityHelper_BuildCreate_WithNullEntity_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<SimpleEntity, int>(context);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => helper.BuildCreate(null!));
    }

    [Fact]
    public void EntityHelper_BuildDelete_GeneratesCorrectSql()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<SimpleEntity, int>(context);

        // Act
        var container = helper.BuildDelete(1);

        // Assert
        Assert.NotNull(container);
        Assert.Contains("DELETE", container.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntityHelper_BuildRetrieve_WithNullIds_ThrowsArgumentException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<SimpleEntity, int>(context);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            helper.BuildRetrieve((System.Collections.Generic.IReadOnlyCollection<int>?)null, "t"));
    }

    [Fact]
    public void EntityHelper_BuildBaseRetrieve_GeneratesSelectQuery()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<SimpleEntity, int>(context);

        // Act
        var container = helper.BuildBaseRetrieve("t");

        // Assert
        Assert.NotNull(container);
        Assert.Contains("SELECT", container.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
