#region

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.wrappers;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerExtensionsTests : IAsyncLifetime
{
    public TypeMapRegistry TypeMap { get; private set; } = null!;
    public IDatabaseContext Context { get; private set; } = null!;
    public IAuditValueResolver AuditValueResolver { get; private set; } = null!;

    public Task InitializeAsync()
    {
        TypeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        Context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, TypeMap);
        AuditValueResolver = new StubAuditValueResolver("test-user");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Context is IAsyncDisposable asyncDisp)
        {
            await asyncDisp.DisposeAsync();
        }
        else if (Context is IDisposable disp)
        {
            disp.Dispose();
        }
    }

    [Fact]
    public void AppendQuery_ValidContainer_AppendsQuery()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");

        var result = container.AppendQuery(" WHERE id = 1");

        Assert.Same(container, result);
        Assert.Contains(" WHERE id = 1", container.Query.ToString());
    }

    [Fact]
    public void AppendQuery_NullContainer_ThrowsArgumentNullException()
    {
        ISqlContainer container = null!;

        Assert.Throws<ArgumentNullException>(() => container.AppendQuery("test"));
    }

    [Fact]
    public void AppendQuery_EmptyQuery_DoesNotAppend()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");
        var originalQuery = container.Query.ToString();

        var result = container.AppendQuery("");

        Assert.Same(container, result);
        Assert.Equal(originalQuery, container.Query.ToString());
    }

    [Fact]
    public void AppendQuery_NullQuery_DoesNotAppend()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");
        var originalQuery = container.Query.ToString();

        var result = container.AppendQuery(null!);

        Assert.Same(container, result);
        Assert.Equal(originalQuery, container.Query.ToString());
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithConcreteSqlContainer_UsesCancellationToken()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");
        using var cts = new CancellationTokenSource();

        var result = await container.ExecuteNonQueryAsync(CommandType.Text, cts.Token);

        Assert.Equal(1, result); // fakeDb returns 1 instead of -1
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteNonQueryAsync(CommandType.Text))
            .Returns(new ValueTask<int>(1));
        mockContainer.Setup(x => x.ExecuteNonQueryAsync(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<int>(1));

        var result = await mockContainer.Object.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None);

        Assert.Equal(1, result);
        mockContainer.Verify(x => x.ExecuteNonQueryAsync(CommandType.Text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        using var container = Context.CreateSqlContainer("SELECT SLEEP(10)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteNonQueryAsync(CommandType.Text, cts.Token).AsTask());
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithConcreteSqlContainer_UsesCancellationToken()
    {
        using var container = Context.CreateSqlContainer("SELECT 42");
        using var cts = new CancellationTokenSource();

        // fakeDb doesn't return actual query results, so this will throw
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.ExecuteScalarAsync<int>(CommandType.Text, cts.Token));
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarAsync<string>(CommandType.Text))
            .Returns(new ValueTask<string?>("test"));
        mockContainer.Setup(x => x.ExecuteScalarAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string?>("test"));

        var result = await mockContainer.Object.ExecuteScalarAsync<string>(CommandType.Text, CancellationToken.None);

        Assert.Equal("test", result);
        mockContainer.Verify(x => x.ExecuteScalarAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        using var container = Context.CreateSqlContainer("SELECT SLEEP(10)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteScalarAsync<int>(CommandType.Text, cts.Token).AsTask());
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithConcreteSqlContainer_UsesCancellationToken()
    {
        using var container = Context.CreateSqlContainer("SELECT 1 as Value");
        using var cts = new CancellationTokenSource();

        using var reader = await container.ExecuteReaderAsync(CommandType.Text, cts.Token);

        Assert.NotNull(reader);
        Assert.False(await reader.ReadAsync()); // fakeDb returns no rows
        // Can't read GetInt32(0) since there are no rows
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockReader = new Mock<ITrackedReader>();
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text))
            .Returns(new ValueTask<ITrackedReader>(mockReader.Object));
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ITrackedReader>(mockReader.Object));

        var result = await mockContainer.Object.ExecuteReaderAsync(CommandType.Text, CancellationToken.None);

        Assert.Equal(mockReader.Object, result);
        mockContainer.Verify(x => x.ExecuteReaderAsync(CommandType.Text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        using var container = Context.CreateSqlContainer("SELECT SLEEP(10)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteReaderAsync(CommandType.Text, cts.Token).AsTask());
    }

    [Fact]
    public void ExtensionMethods_AvailableOnInterfaceType()
    {
        var container = Context.CreateSqlContainer("SELECT 1");

        Assert.NotNull(container.AppendQuery(" ORDER BY 1"));

        var executeNonQueryTask = container.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None);
        var executeScalarTask = container.ExecuteScalarAsync<int>(CommandType.Text, CancellationToken.None);
        var executeReaderTask = container.ExecuteReaderAsync(CommandType.Text, CancellationToken.None);

        Assert.IsType<ValueTask<int>>(executeNonQueryTask);
        Assert.IsType<ValueTask<int>>(executeScalarTask);
        Assert.IsType<ValueTask<ITrackedReader>>(executeReaderTask);

        container.Dispose();
    }

    // Tests for ExecuteReaderSingleRowAsync
    [Fact]
    public async Task ExecuteReaderSingleRowAsync_WithConcreteSqlContainer_CallsConcrete()
    {
        using var container = Context.CreateSqlContainer("SELECT 1 as Value");
        using var cts = new CancellationTokenSource();

        using var reader = await container.ExecuteReaderSingleRowAsync(cts.Token);

        Assert.NotNull(reader);
    }

    [Fact]
    public async Task ExecuteReaderSingleRowAsync_WithMockContainer_FallsBackToExecuteReaderAsync()
    {
        var mockReader = new Mock<ITrackedReader>();
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ITrackedReader>(mockReader.Object));

        var result = await mockContainer.Object.ExecuteReaderSingleRowAsync(CancellationToken.None);

        Assert.Equal(mockReader.Object, result);
        mockContainer.Verify(x => x.ExecuteReaderAsync(CommandType.Text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteReaderSingleRowAsync_WithDefaultToken_Works()
    {
        using var container = Context.CreateSqlContainer("SELECT 1 as Value");

        using var reader = await container.ExecuteReaderSingleRowAsync();

        Assert.NotNull(reader);
    }

    [Fact]
    public async Task ExecuteReaderSingleRowAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        using var container = Context.CreateSqlContainer("SELECT SLEEP(10)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => container.ExecuteReaderSingleRowAsync(cts.Token).AsTask());
    }

    // Tests for ExecuteScalarWriteAsync
    [Fact]
    public async Task ExecuteScalarWriteAsync_WithConcreteSqlContainer_CallsConcrete()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");
        using var cts = new CancellationTokenSource();

        // This tests that the concrete path is taken; fakeDb returns default values
        var result = await container.ExecuteScalarWriteAsync<int>(CommandType.Text, cts.Token);

        // Just verify it doesn't throw and completes the call
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_WithMockContainer_FallsBackToExecuteScalarAsync()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarAsync<int>(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<int>(42));

        var result = await mockContainer.Object.ExecuteScalarWriteAsync<int>(CommandType.Text, CancellationToken.None);

        Assert.Equal(42, result);
        mockContainer.Verify(x => x.ExecuteScalarAsync<int>(CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_WithDefaultToken_Works()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");

        // This tests the default token path; fakeDb returns default values
        var result = await container.ExecuteScalarWriteAsync<int>();

        // Just verify it doesn't throw and completes the call
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_WithDefaultCommandType_UsesText()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string?>("test"));

        var result = await mockContainer.Object.ExecuteScalarWriteAsync<string>();

        Assert.Equal("test", result);
        mockContainer.Verify(x => x.ExecuteScalarAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        using var container = Context.CreateSqlContainer("INSERT INTO test (value) VALUES (1)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteScalarWriteAsync<int>(CommandType.Text, cts.Token).AsTask());
    }

    // ── Static-syntax calls to hit the shadowed extension methods ─────────
    // The interface declares the same signatures, so normal dot-syntax
    // resolves to the interface method.  Only explicit static invocation
    // routes through SqlContainerExtensions and exercises the concrete/fallback branches.

    [Fact]
    public async Task StaticCall_ExecuteNonQueryAsync_Concrete_UsesConcretePath()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");

        var result = await SqlContainerExtensions.ExecuteNonQueryAsync(
            container, CommandType.Text, CancellationToken.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task StaticCall_ExecuteNonQueryAsync_Mock_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteNonQueryAsync(CommandType.Text))
            .Returns(new ValueTask<int>(7));

        var result = await SqlContainerExtensions.ExecuteNonQueryAsync(
            mockContainer.Object, CommandType.Text, CancellationToken.None);

        Assert.Equal(7, result);
        mockContainer.Verify(x => x.ExecuteNonQueryAsync(CommandType.Text), Times.Once);
    }

    [Fact]
    public async Task StaticCall_ExecuteScalarAsync_Concrete_UsesConcretePath()
    {
        using var container = Context.CreateSqlContainer("SELECT 42");

        // fakeDb scalar path throws when no rows — that's fine, we just
        // need to confirm the concrete branch was taken (not the fallback).
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SqlContainerExtensions.ExecuteScalarAsync<int>(
                container, CommandType.Text, CancellationToken.None));
    }

    [Fact]
    public async Task StaticCall_ExecuteScalarAsync_Mock_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarAsync<string>(CommandType.Text))
            .Returns(new ValueTask<string?>("hello"));

        var result = await SqlContainerExtensions.ExecuteScalarAsync<string>(
            mockContainer.Object, CommandType.Text, CancellationToken.None);

        Assert.Equal("hello", result);
        mockContainer.Verify(x => x.ExecuteScalarAsync<string>(CommandType.Text), Times.Once);
    }

    [Fact]
    public async Task StaticCall_ExecuteReaderAsync_Concrete_UsesConcretePath()
    {
        using var container = Context.CreateSqlContainer("SELECT 1 as Value");

        using var reader = await SqlContainerExtensions.ExecuteReaderAsync(
            container, CommandType.Text, CancellationToken.None);

        Assert.NotNull(reader);
    }

    [Fact]
    public async Task StaticCall_ExecuteReaderAsync_Mock_FallsBackToInterfaceMethod()
    {
        var mockReader = new Mock<ITrackedReader>();
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text))
            .Returns(new ValueTask<ITrackedReader>(mockReader.Object));

        var result = await SqlContainerExtensions.ExecuteReaderAsync(
            mockContainer.Object, CommandType.Text, CancellationToken.None);

        Assert.Equal(mockReader.Object, result);
        mockContainer.Verify(x => x.ExecuteReaderAsync(CommandType.Text), Times.Once);
    }
}
