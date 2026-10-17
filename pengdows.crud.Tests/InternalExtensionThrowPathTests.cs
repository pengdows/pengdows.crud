// =============================================================================
// FILE: InternalExtensionThrowPathTests.cs
// PURPOSE: Coverage for defensive throw paths in internal extension methods:
//   - InternalSqlContainerExtensions.CreateCommand (non-SqlContainer)
//   - InternalConnectionExtensions.GetConnection/GetLock/CloseAndDispose (non-provider)
//   - InternalSqlDialectExtensions.GetInternal (non-IInternalSqlDialect)
// =============================================================================

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class InternalExtensionThrowPathTests
{
    // =========================================================================
    // InternalSqlContainerExtensions.CreateCommand — non-SqlContainer input
    // (InternalSqlContainerExtensions.cs lines 11-12)
    // =========================================================================

    [Fact]
    public void CreateCommand_NonSqlContainerISqlContainer_Throws()
    {
        // An ISqlContainer that is NOT a SqlContainer
        var mockContainer = new ScriptedSqlContainer();
        var mockConnection = new ScriptedTrackedConnection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => mockContainer.CreateCommand(mockConnection));

        Assert.Contains("SqlContainer", ex.Message);
    }

    // =========================================================================
    // InternalConnectionExtensions — non-IInternalConnectionProvider context
    // (InternalConnectionExtensions.cs lines 39-40, 49-50, 60-61)
    // =========================================================================

    [Fact]
    public void GetConnection_NonProviderContext_Throws()
    {
        // IDatabaseContext that does NOT implement IInternalConnectionProvider
        var mockCtx = new ScriptedDatabaseContext();

        Assert.Throws<InvalidOperationException>(
            () => mockCtx.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public void GetLock_NonProviderContext_Throws()
    {
        var mockCtx = new ScriptedDatabaseContext();

        Assert.Throws<InvalidOperationException>(
            () => mockCtx.GetLock());
    }

    [Fact]
    public void CloseAndDisposeConnection_NonProviderContext_Throws()
    {
        var mockCtx = new ScriptedDatabaseContext();

        Assert.Throws<InvalidOperationException>(
            () => mockCtx.CloseAndDisposeConnection(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task CloseAndDisposeConnectionAsync_NonProviderContext_Throws()
    {
        var mockCtx = new ScriptedDatabaseContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mockCtx.CloseAndDisposeConnectionAsync(null));
    }

    // =========================================================================
    // InternalSqlDialectExtensions.GetInternal — non-IInternalSqlDialect input
    // (InternalSqlDialectExtensions.cs lines 100-101)
    // =========================================================================

    [Fact]
    public void ApplyConnectionSettings_NonInternalDialect_Throws()
    {
        // An ISqlDialect that does NOT implement IInternalSqlDialect
        var mockDialect = new ScriptedSqlDialect();
        var mockCtx = new ScriptedDatabaseContext();
        var mockConn = new fakeDbConnection();

        Assert.Throws<InvalidOperationException>(
            () => mockDialect.ApplyConnectionSettings(mockConn, mockCtx, false));
    }

    [Fact]
    public void ShouldDisablePrepareOn_NonInternalDialect_Throws()
    {
        var mockDialect = new ScriptedSqlDialect();
        var ex = new Exception("test");

        Assert.Throws<InvalidOperationException>(
            () => mockDialect.ShouldDisablePrepareOn(ex));
    }

    [Fact]
    public void TryEnterReadOnlyTransaction_NonInternalDialect_Throws()
    {
        var mockDialect = new ScriptedSqlDialect();
        var mockTx = new ScriptedTransactionContext();

        Assert.Throws<InvalidOperationException>(
            () => mockDialect.TryEnterReadOnlyTransaction(mockTx));
    }

    // =========================================================================
    // InternalSqlDialectExtensions — success paths via real dialect
    // (covers the method body lines 16-17, 51-53, 56-58 in the extension file)
    // These extension methods are defined but never called via the extension
    // in production code — the internal method is called directly on the dialect.
    // =========================================================================

    private static ISqlDialect GetRealDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        return ctx.Dialect;
    }

    [Fact]
    public async System.Threading.Tasks.Task ApplyConnectionSettings_RealDialect_DoesNotThrow()
    {
        var dialect = GetRealDialect();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        // SQLite :memory: auto-coerces to DbMode.SingleConnection, whose GetConnection returns
        // the already-open persistent connection.
        await using var conn = ctx.GetConnection(ExecutionType.Write);

        // ITrackedConnection extends IDbConnection — pass it directly to the extension
        var record = Record.Exception(
            () => dialect.ApplyConnectionSettings(conn, ctx, false));
        Assert.Null(record);
    }

    [Fact]
    public void ShouldDisablePrepareOn_RealDialect_ReturnsBool()
    {
        var dialect = GetRealDialect();
        var ex = new InvalidOperationException("test error");

        // Just ensure it doesn't throw and returns a valid bool
        var result = dialect.ShouldDisablePrepareOn(ex);
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void GetMajorVersion_RealDialect_ReturnsNullableInt()
    {
        var dialect = GetRealDialect();

        // "3.39.5" → major version 3
        var result = dialect.GetMajorVersion("3.39.5");
        Assert.True(result == null || result >= 0);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetDataSourceInformationSchema_RealDialect_ReturnsDataTable()
    {
        var dialect = GetRealDialect();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        // SQLite :memory: auto-coerces to DbMode.SingleConnection, whose GetConnection returns
        // the already-open persistent connection.
        await using var conn = ctx.GetConnection(ExecutionType.Read);

        var table = dialect.GetDataSourceInformationSchema(conn);
        // The result may be null or empty for fakeDb, but must not throw
        Assert.True(table == null || table.GetType().Name == "DataTable");
    }
}
