using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.strategies.connection;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class LowCoverageBranchTests
{
    [Fact]
    public void ContextBase_DefaultLoggerResolverAndForwarders_AreExercised()
    {
        var dialect = new Mock<ISqlDialect>();
        dialect.Setup(d => d.GenerateParameterName()).Returns("p42");

        var context = new ContextBaseHarness(dialect.Object);

        Assert.Null(context.ResolveLoggerForTest());
        Assert.Equal("p42", context.GenerateParameterName());
        Assert.Equal(0, context.MaxOutputParameters);
    }

    [Fact]
    public async Task StandardConnectionStrategy_ReleaseNonPersistentConnectionAsync_DisposesNonAsyncConnection()
    {
        var tracked = new Mock<ITrackedConnection>();

        var result = StandardConnectionStrategy.ReleaseNonPersistentConnectionAsync(tracked.Object, null);
        await result;

        tracked.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void StandardConnectionStrategy_HandleDialectDetection_WithNullInputs_ReturnsNullTuple()
    {
        using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=std-strategy.db;EmulatedProduct=Sqlite",
                DbMode = DbMode.Standard,
                ReadWriteMode = ReadWriteMode.ReadWrite
            },
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var strategy = new StandardConnectionStrategy(context);

        var result = strategy.HandleDialectDetection(null, null, NullLoggerFactory.Instance);

        Assert.Null(result.dialect);
        Assert.Null(result.dataSourceInfo);
    }

    [Fact]
    public void ReflectionSerializer_HandlesNullAndStringAndArrayDeserializeBranches()
    {
        Assert.Null(ReflectionSerializer.Serialize(null));
        Assert.Equal("plain", ReflectionSerializer.Serialize("plain"));

        var deserialize = typeof(ReflectionSerializer).GetMethod(
            "Deserialize",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            binder: null,
            new[] { typeof(Type), typeof(object) },
            modifiers: null) ?? throw new InvalidOperationException("Deserialize(Type, object) not found.");

        var nullResult = deserialize.Invoke(null, new object?[] { typeof(int), null });
        Assert.Null(nullResult);

        var stringResult = (string?)deserialize.Invoke(null, new object[] { typeof(string), 123 });
        Assert.Equal("123", stringResult);

        var arrayResult = (int[]?)deserialize.Invoke(null, new object[]
        {
            typeof(int[]),
            new List<object?> { 1, 2, 3 }
        });
        Assert.Equal(new[] { 1, 2, 3 }, arrayResult);
    }

    [Fact]
    public void ModeContentionException_ExposesTimeoutProperty()
    {
        var snapshot = new ModeContentionSnapshot(
            CurrentWaiters: 3,
            PeakWaiters: 5,
            TotalWaits: 9,
            TotalTimeouts: 2,
            TotalWaitTimeTicks: 42,
            AverageWaitTimeTicks: 7);
        var timeout = TimeSpan.FromSeconds(4);

        var ex = new ModeContentionException(DbMode.SingleWriter, snapshot, timeout);

        Assert.Equal(timeout, ex.Timeout);
    }

    private sealed class ContextBaseHarness : ContextBase
    {
        private readonly ISqlDialect _dialect;

        public ContextBaseHarness(ISqlDialect dialect)
        {
            _dialect = dialect;
        }

        protected override ISqlDialect DialectCore => _dialect;

        public ILogger<ISqlContainer>? ResolveLoggerForTest()
        {
            return ResolveSqlContainerLogger();
        }
    }
}
