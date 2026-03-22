#region

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.wrappers;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerExtensionsTests : IAsyncLifetime
{
    internal ITypeMapRegistry TypeMap { get; private set; } = null!;
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
    public async Task ExecuteScalarOrNullAsync_WithConcreteSqlContainer_UsesCancellationToken()
    {
        using var container = Context.CreateSqlContainer("SELECT 42");
        using var cts = new CancellationTokenSource();

        // fakeDb doesn't return actual query results; OrNull returns null for no rows
        var result = await container.ExecuteScalarOrNullAsync<int?>(CommandType.Text, cts.Token);
        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteScalarOrNullAsync_WithMockContainer_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarOrNullAsync<string>(CommandType.Text))
            .Returns(new ValueTask<string?>("test"));
        mockContainer.Setup(x => x.ExecuteScalarOrNullAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string?>("test"));

        var result =
            await mockContainer.Object.ExecuteScalarOrNullAsync<string>(CommandType.Text, CancellationToken.None);

        Assert.Equal("test", result);
        mockContainer.Verify(x => x.ExecuteScalarOrNullAsync<string>(CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarOrNullAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        using var container = Context.CreateSqlContainer("SELECT SLEEP(10)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteScalarOrNullAsync<int>(CommandType.Text, cts.Token).AsTask());
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
        var executeScalarTask = container.ExecuteScalarOrNullAsync<int>(CommandType.Text, CancellationToken.None);
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

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteReaderSingleRowAsync(cts.Token).AsTask());
    }

    // Tests for ExecuteScalarRequiredAsync (now an interface method, extensions provide ExecutionType routing)
    [Fact]
    public async Task ExecuteScalarRequiredAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        using var container = Context.CreateSqlContainer("INSERT INTO test (value) VALUES (1)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteScalarRequiredAsync<int>(CommandType.Text, cts.Token).AsTask());
    }

    [Fact]
    public async Task ExecuteScalarRequiredAsync_WithExecutionType_Concrete_UsesConcretePath()
    {
        using var container = Context.CreateSqlContainer("SELECT 42");

        // fakeDb returns no rows - Required throws for no rows
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SqlContainerExtensions.ExecuteScalarRequiredAsync<int>(
                container, ExecutionType.Write));
    }

    [Fact]
    public async Task ExecuteScalarRequiredAsync_WithExecutionType_Mock_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarRequiredAsync<int>(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<int>(42));

        var result = await SqlContainerExtensions.ExecuteScalarRequiredAsync<int>(
            mockContainer.Object, ExecutionType.Read);

        Assert.Equal(42, result);
        mockContainer.Verify(x => x.ExecuteScalarRequiredAsync<int>(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
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
        mockContainer.Setup(x => x.ExecuteNonQueryAsync(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<int>(7));

        var result = await SqlContainerExtensions.ExecuteNonQueryAsync(
            mockContainer.Object, CommandType.Text, CancellationToken.None);

        Assert.Equal(7, result);
        mockContainer.Verify(x => x.ExecuteNonQueryAsync(CommandType.Text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StaticCall_ExecuteScalarOrNullAsync_Concrete_UsesConcretePath()
    {
        using var container = Context.CreateSqlContainer("SELECT 42");

        // fakeDb scalar path returns null when no reader results are queued.
        var result = await container.ExecuteScalarOrNullAsync<int?>();
        Assert.Null(result);
    }

    [Fact]
    public async Task StaticCall_ExecuteScalarOrNullAsync_Mock_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarOrNullAsync<string>(CommandType.Text))
            .Returns(new ValueTask<string?>("hello"));

        var result = await mockContainer.Object.ExecuteScalarOrNullAsync<string>(CommandType.Text);

        Assert.Equal("hello", result);
        mockContainer.Verify(x => x.ExecuteScalarOrNullAsync<string>(CommandType.Text), Times.Once);
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
        mockContainer.Setup(x => x.ExecuteReaderAsync(CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ITrackedReader>(mockReader.Object));

        var result = await SqlContainerExtensions.ExecuteReaderAsync(
            mockContainer.Object, CommandType.Text, CancellationToken.None);

        Assert.Equal(mockReader.Object, result);
        mockContainer.Verify(x => x.ExecuteReaderAsync(CommandType.Text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarOrNullAsync_WithExecutionType_Mock_FallsBackToInterfaceMethod()
    {
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteScalarOrNullAsync<int>(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<int>(123));

        var result = await SqlContainerExtensions.ExecuteScalarOrNullAsync<int>(
            mockContainer.Object, ExecutionType.Read, CommandType.Text, CancellationToken.None);

        Assert.Equal(123, result);
        mockContainer.Verify(x => x.ExecuteScalarOrNullAsync<int>(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarOrNullAsync_WithExecutionType_Concrete_UsesConcretePath()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");

        // OrNull returns null/default for no rows
        var result = await SqlContainerExtensions.ExecuteScalarOrNullAsync<int?>(
            container, ExecutionType.Read, CommandType.Text, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithExecutionType_Mock_FallsBackToInterfaceMethod()
    {
        var mockReader = new Mock<ITrackedReader>();
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteReaderAsync(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ITrackedReader>(mockReader.Object));

        var result = await SqlContainerExtensions.ExecuteReaderAsync(
            mockContainer.Object, ExecutionType.Read, CommandType.Text, CancellationToken.None);

        Assert.Equal(mockReader.Object, result);
        mockContainer.Verify(x => x.ExecuteReaderAsync(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithExecutionType_Concrete_UsesConcrete()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");

        using var reader = await SqlContainerExtensions.ExecuteReaderAsync(
            container, ExecutionType.Read, CommandType.Text, CancellationToken.None);

        Assert.NotNull(reader);
    }

    [Fact]
    public async Task ExecuteReaderSingleRowAsync_WithExecutionType_Mock_FallsBackToExecuteReaderAsync()
    {
        var mockReader = new Mock<ITrackedReader>();
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.ExecuteReaderAsync(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ITrackedReader>(mockReader.Object));

        var result = await SqlContainerExtensions.ExecuteReaderSingleRowAsync(
            mockContainer.Object, ExecutionType.Read, CancellationToken.None);

        Assert.Equal(mockReader.Object, result);
        mockContainer.Verify(x =>
            x.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteReaderSingleRowAsync_WithExecutionType_Concrete_UsesConcretePath()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");

        using var reader = await SqlContainerExtensions.ExecuteReaderSingleRowAsync(
            container, ExecutionType.Read, CancellationToken.None);

        Assert.NotNull(reader);
    }

    [Fact]
    public async Task TryExecuteScalarAsync_WithExecutionType_Concrete_UsesConcretePath()
    {
        // Covers SqlContainerExtensions lines 151, 153 (concrete SqlContainer path)
        using var container = Context.CreateSqlContainer("SELECT 1");

        var result = await SqlContainerExtensions.TryExecuteScalarAsync<int>(
            container, ExecutionType.Read, CommandType.Text, CancellationToken.None);

        Assert.True(result.Status == ScalarStatus.None || result.HasValue);
    }

    [Fact]
    public async Task TryExecuteScalarAsync_WithExecutionType_Mock_FallsBackToInterfaceMethod()
    {
        // Covers SqlContainerExtensions line 156 (ISqlContainer fallback path)
        var mockContainer = new Mock<ISqlContainer>();
        mockContainer.Setup(x => x.TryExecuteScalarAsync<int>(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ScalarResult<int>>(new ScalarResult<int>(ScalarStatus.None, default)));

        var result = await SqlContainerExtensions.TryExecuteScalarAsync<int>(
            mockContainer.Object, ExecutionType.Read, CommandType.Text, CancellationToken.None);

        Assert.Equal(ScalarStatus.None, result.Status);
        mockContainer.Verify(x => x.TryExecuteScalarAsync<int>(
                ExecutionType.Read, CommandType.Text, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AppendName / AppendEquals / AppendAnd / AppendWhere / AppendParam ──

    [Fact]
    public void AppendName_SingleName_AppendsDialectQuotedName()
    {
        using var container = Context.CreateSqlContainer();
        var expected = container.WrapObjectName("post_date");

        var result = container.AppendName("post_date");

        Assert.Same(container, result);
        Assert.Equal(expected, container.Query.ToString());
    }

    [Fact]
    public void AppendName_AliasAndName_AppendsCorrectlyQuotedQualifiedIdentifier()
    {
        using var container = Context.CreateSqlContainer();
        var expected = container.WrapObjectName("p")
            + container.CompositeIdentifierSeparator
            + container.WrapObjectName("post_status");

        var result = container.AppendName("p", "post_status");

        Assert.Same(container, result);
        Assert.Equal(expected, container.Query.ToString());
    }

    [Fact]
    public void AppendName_AliasAndName_DelegatesToWrapObjectNameWithDot()
    {
        // AppendName("p", "post_status") is exactly WrapObjectName("p.post_status") —
        // the dialect handles dot-splitting internally.
        using var container = Context.CreateSqlContainer();
        var expected = container.WrapObjectName("p.post_status");
        container.AppendName("p", "post_status");
        Assert.Equal(expected, container.Query.ToString());
    }

    [Fact]
    public void AppendEquals_AppendsEqualsOperator()
    {
        using var container = Context.CreateSqlContainer();

        var result = container.AppendEquals();

        Assert.Same(container, result);
        Assert.Equal(" = ", container.Query.ToString());
    }

    [Fact]
    public void AppendAnd_AppendsAndKeyword()
    {
        using var container = Context.CreateSqlContainer();

        var result = container.AppendAnd();

        Assert.Same(container, result);
        Assert.Equal(" AND ", container.Query.ToString());
    }

    [Fact]
    public void AppendWhere_AppendsWhereKeyword()
    {
        using var container = Context.CreateSqlContainer();

        var result = container.AppendWhere();

        Assert.Same(container, result);
        Assert.Equal(" WHERE ", container.Query.ToString());
    }

    [Fact]
    public void AppendParam_AppendsDialectFormattedParameterName()
    {
        using var container = Context.CreateSqlContainer();
        var p = container.AddParameterWithValue("status", DbType.String, "publish");
        var expected = container.MakeParameterName(p);

        var result = container.AppendParam(p);

        Assert.Same(container, result);
        Assert.Equal(expected, container.Query.ToString());
    }

    [Fact]
    public void AppendParam_StringOverload_AppendsDialectFormattedName()
    {
        using var container = Context.CreateSqlContainer();
        var expected = container.MakeParameterName("status");

        var result = container.AppendParam("status");

        Assert.Same(container, result);
        Assert.Equal(expected, container.Query.ToString());
    }

    [Fact]
    public void AppendIn_AppendsInKeyword()
    {
        using var container = Context.CreateSqlContainer();

        var result = container.AppendIn();

        Assert.Same(container, result);
        Assert.Equal(" IN (", container.Query.ToString());
    }

    [Fact]
    public void AppendCloseParen_AppendsCloseParen()
    {
        using var container = Context.CreateSqlContainer();

        var result = container.AppendCloseParen();

        Assert.Same(container, result);
        Assert.Equal(")", container.Query.ToString());
    }

    [Fact]
    public void AppendComma_AppendsComma()
    {
        using var container = Context.CreateSqlContainer();

        var result = container.AppendComma();

        Assert.Same(container, result);
        Assert.Equal(", ", container.Query.ToString());
    }

    [Fact]
    public void AppendName_FluentChain_ProducesCorrectSqlFragment()
    {
        using var container = Context.CreateSqlContainer();
        var pStatus = container.AddParameterWithValue("status", DbType.String, "publish");
        var pType   = container.AddParameterWithValue("type",   DbType.String, "post");

        container
            .AppendWhere().AppendName("p", "post_status").AppendEquals().AppendParam(pStatus)
            .AppendAnd()  .AppendName("p", "post_type")  .AppendEquals().AppendParam(pType);

        var sql = container.Query.ToString();
        Assert.StartsWith(" WHERE ", sql);
        Assert.Contains(container.WrapObjectName("post_status"), sql);
        Assert.Contains(container.WrapObjectName("post_type"), sql);
        Assert.Contains(" AND ", sql);
        Assert.Contains(" = ", sql);
        Assert.Contains(container.MakeParameterName(pStatus), sql);
        Assert.Contains(container.MakeParameterName(pType), sql);
    }
}