#region

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerExtensionsTests : SqlLiteContextTestBase
{
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
        
        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteNonQueryAsync(CommandType.Text))
                    .ReturnsAsync(1);
        
        var result = await mockContainer.Object.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None);
        
        Assert.Equal(1, result);
        mockContainer.Verify(x => x.ExecuteNonQueryAsync(CommandType.Text), Times.Once);
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
        
        var result = await container.ExecuteScalarAsync<int>(CommandType.Text, cts.Token);
        
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarAsync<string>(CommandType.Text))
                    .ReturnsAsync("test");
        
        var result = await mockContainer.Object.ExecuteScalarAsync<string>(CommandType.Text, CancellationToken.None);
        
        Assert.Equal("test", result);
        mockContainer.Verify(x => x.ExecuteScalarAsync<string>(CommandType.Text), Times.Once);
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
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockReader = new Mock<ITrackedReader>();
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text))
                    .ReturnsAsync(mockReader.Object);
        
        var result = await mockContainer.Object.ExecuteReaderAsync(CommandType.Text, CancellationToken.None);
        
        Assert.Equal(mockReader.Object, result);
        mockContainer.Verify(x => x.ExecuteReaderAsync(CommandType.Text), Times.Once);
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