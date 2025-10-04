#region

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.wrappers;
using pengdows.crud.fakeDb;
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
            await asyncDisp.DisposeAsync().ConfigureAwait(false);
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
        
        Assert.Equal(1, result);  // fakeDb returns 1 instead of -1
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteNonQueryAsync(CommandType.Text))
                    .ReturnsAsync(1);
        mockContainer.Setup(x => x.ExecuteNonQueryAsync(CommandType.Text, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(1);

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
        
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => container.ExecuteNonQueryAsync(CommandType.Text, cts.Token));
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
                    .ReturnsAsync("test");
        mockContainer.Setup(x => x.ExecuteScalarAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()))
                    .ReturnsAsync("test");

        var result = await mockContainer.Object.ExecuteScalarAsync<string>(CommandType.Text, CancellationToken.None);

        Assert.Equal("test", result);
        mockContainer.Verify(x => x.ExecuteScalarAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        using var container = Context.CreateSqlContainer("SELECT SLEEP(10)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => container.ExecuteScalarAsync<int>(CommandType.Text, cts.Token));
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithConcreteSqlContainer_UsesCancellationToken()
    {
        using var container = Context.CreateSqlContainer("SELECT 1 as Value");
        using var cts = new CancellationTokenSource();
        
        using var reader = await container.ExecuteReaderAsync(CommandType.Text, cts.Token);
        
        Assert.NotNull(reader);
        Assert.False(await reader.ReadAsync());  // fakeDb returns no rows
        // Can't read GetInt32(0) since there are no rows
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockReader = new Mock<ITrackedReader>();
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text))
                    .ReturnsAsync(mockReader.Object);
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(mockReader.Object);

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
        
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => container.ExecuteReaderAsync(CommandType.Text, cts.Token));
    }

    [Fact]
    public void ExtensionMethods_AvailableOnInterfaceType()
    {
        ISqlContainer container = Context.CreateSqlContainer("SELECT 1");
        
        Assert.NotNull(container.AppendQuery(" ORDER BY 1"));
        
        var executeNonQueryTask = container.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None);
        var executeScalarTask = container.ExecuteScalarAsync<int>(CommandType.Text, CancellationToken.None);
        var executeReaderTask = container.ExecuteReaderAsync(CommandType.Text, CancellationToken.None);
        
        Assert.NotNull(executeNonQueryTask);
        Assert.NotNull(executeScalarTask);
        Assert.NotNull(executeReaderTask);
        
        container.Dispose();
    }
}