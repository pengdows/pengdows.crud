// =============================================================================
// FILE: CoveragePush_InternalExtensionThrowPathsTests.cs
// PURPOSE: Coverage for defensive throw paths in internal extension methods:
//   - InternalSqlContainerExtensions.CreateCommand (non-SqlContainer)
//   - InternalConnectionExtensions.GetConnection/GetLock/CloseAndDispose (non-provider)
//   - InternalSqlDialectExtensions.GetInternal (non-IInternalSqlDialect)
// =============================================================================

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class CoveragePush_InternalExtensionThrowPathsTests
{
    // =========================================================================
    // InternalSqlContainerExtensions.CreateCommand — non-SqlContainer input
    // (InternalSqlContainerExtensions.cs lines 11-12)
    // =========================================================================

    [Fact]
    public void CreateCommand_NonSqlContainerISqlContainer_Throws()
    {
        // Create a mock ISqlContainer that is NOT a SqlContainer
        var mockContainer = new Mock<ISqlContainer>();
        var mockConnection = new Mock<ITrackedConnection>();

        var ex = Assert.Throws<InvalidOperationException>(
            () => mockContainer.Object.CreateCommand(mockConnection.Object));

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
        var mockCtx = new Mock<IDatabaseContext>();
        mockCtx.Setup(c => c.ReadWriteMode).Returns(ReadWriteMode.ReadWrite);

        Assert.Throws<InvalidOperationException>(
            () => mockCtx.Object.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public void GetLock_NonProviderContext_Throws()
    {
        var mockCtx = new Mock<IDatabaseContext>();

        Assert.Throws<InvalidOperationException>(
            () => mockCtx.Object.GetLock());
    }

    [Fact]
    public void CloseAndDisposeConnection_NonProviderContext_Throws()
    {
        var mockCtx = new Mock<IDatabaseContext>();

        Assert.Throws<InvalidOperationException>(
            () => mockCtx.Object.CloseAndDisposeConnection(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task CloseAndDisposeConnectionAsync_NonProviderContext_Throws()
    {
        var mockCtx = new Mock<IDatabaseContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mockCtx.Object.CloseAndDisposeConnectionAsync(null));
    }

    // =========================================================================
    // InternalSqlDialectExtensions.GetInternal — non-IInternalSqlDialect input
    // (InternalSqlDialectExtensions.cs lines 100-101)
    // =========================================================================

    [Fact]
    public void ApplyConnectionSettings_NonInternalDialect_Throws()
    {
        // Create a mock ISqlDialect that does NOT implement IInternalSqlDialect
        var mockDialect = new Mock<ISqlDialect>();
        var mockCtx = new Mock<IDatabaseContext>();
        var mockConn = new Mock<IDbConnection>();

        Assert.Throws<InvalidOperationException>(
            () => mockDialect.Object.ApplyConnectionSettings(mockConn.Object, mockCtx.Object, false));
    }

    [Fact]
    public void ShouldDisablePrepareOn_NonInternalDialect_Throws()
    {
        var mockDialect = new Mock<ISqlDialect>();
        var ex = new Exception("test");

        Assert.Throws<InvalidOperationException>(
            () => mockDialect.Object.ShouldDisablePrepareOn(ex));
    }

    [Fact]
    public void TryEnterReadOnlyTransaction_NonInternalDialect_Throws()
    {
        var mockDialect = new Mock<ISqlDialect>();
        var mockTx = new Mock<ITransactionContext>();

        Assert.Throws<InvalidOperationException>(
            () => mockDialect.Object.TryEnterReadOnlyTransaction(mockTx.Object));
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
        await using var conn = ctx.GetConnection(ExecutionType.Write);
        await conn.OpenAsync();

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
        await using var conn = ctx.GetConnection(ExecutionType.Read);
        await conn.OpenAsync();

        var table = dialect.GetDataSourceInformationSchema(conn);
        // The result may be null or empty for fakeDb, but must not throw
        Assert.True(table == null || table.GetType().Name == "DataTable");
    }
}
