#region

using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// In DbMode.SingleConnection a transaction holds the single-connection transaction gate. A
/// read-only transaction whose sync begin fails must release that gate; otherwise every later
/// transaction on the context blocks until ModeLockTimeout (or forever).
/// </summary>
public class TransactionReadOnlyFailureGateTests
{
    private sealed class SingleConnectionFailingReadOnlyDialect : Sql92Dialect
    {
        private int _failuresRemaining = 1;

        public SingleConnectionFailingReadOnlyDialect(DbProviderFactory factory, ILogger logger)
            : base(factory, logger)
        {
        }

        public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
        {
            if (_failuresRemaining-- > 0)
            {
                throw new InvalidOperationException("Simulated read-only session configuration failure.");
            }
        }
    }

    [Fact]
    public async Task SyncReadOnlyBeginFailure_ReleasesSingleConnectionGate()
    {
        // SQLite :memory: resolves to SingleConnection; the dialect override only drives the
        // transaction path (mode coercion always uses the detected product's own dialect).
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SingleConnectionFailingReadOnlyDialect(factory, NullLogger.Instance);
        using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory,
            new TypeMapRegistry(), dialect);
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);

        Assert.Throws<InvalidOperationException>(() =>
            context.BeginTransaction(IsolationLevel.Serializable, ExecutionType.Read));

        // With the gate leaked this blocks; bound the wait so the test fails instead of hanging.
        var next = Task.Run(() =>
        {
            using var tx = context.BeginTransaction(IsolationLevel.Serializable, ExecutionType.Write);
            tx.Commit();
        });
        var finished = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(next, finished);
        await next;
    }
}
