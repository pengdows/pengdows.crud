using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class GatewayMethodContextParameterAnalyzerTests
{
    [Fact]
    public async Task PublicGatewayMethodThatExecutesWithoutContextParameter_ProducesDiagnostic()
    {
        var source = """
            using System;
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
                Task<ITransactionContext> BeginTransactionAsync();
            }

            public interface ITransactionContext : IDatabaseContext, IAsyncDisposable
            {
                Task CommitAsync();
                Task RollbackAsync();
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<TEntity?> RetrieveOneAsync(TId id) => throw new System.NotImplementedException();
                protected Task<int> UpdateAsync(TEntity entity) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task CancelOrderAsync(long orderId)
                {
                    await using var tx = await Context.BeginTransactionAsync();
                    var order = await RetrieveOneAsync(orderId);
                    if (order is null)
                    {
                        await tx.RollbackAsync();
                        return;
                    }

                    await UpdateAsync(order);
                    await tx.CommitAsync();
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task PublicGatewayMethodThatAcceptsContextParameter_DoesNotProduceDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected Task<TEntity?> RetrieveOneAsync(TId id, IDatabaseContext context) => throw new System.NotImplementedException();
                protected Task<int> UpdateAsync(TEntity entity, IDatabaseContext context) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task CancelOrderAsync(long orderId, IDatabaseContext? context = null)
                {
                    var ctx = context ?? Context;
                    var order = await RetrieveOneAsync(orderId, ctx);
                    if (order is null)
                    {
                        return;
                    }

                    await UpdateAsync(order, ctx);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task PrivateGatewayHelperWithoutContextParameter_DoesNotProduceDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public class TableGateway<TEntity, TId>
            {
                protected Task<TEntity?> RetrieveOneAsync(TId id) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                private async Task CancelOrderAsync(long orderId)
                {
                    await RetrieveOneAsync(orderId);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task PublicGatewayMethodThatAcceptsContextAndPassesItDirect_DoesNotProduceDiagnostic()
    {
        // Passing context! directly (without coalesce) is compliant — equivalent to coalesced form.
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<TEntity?> RetrieveOneAsync(TId id, IDatabaseContext context) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task CancelOrderAsync(long orderId, IDatabaseContext? context = null)
                {
                    await RetrieveOneAsync(orderId, context!);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task PublicGatewayMethodThatAcceptsContextButIgnoresIt_ProducesDiagnostic()
    {
        // context parameter accepted but never forwarded to execution calls.
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<TEntity?> RetrieveOneAsync(TId id) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task<Order?> GetOrderAsync(long orderId, IDatabaseContext? context = null)
                {
                    // context is accepted but not forwarded — any transaction is silently ignored
                    return await RetrieveOneAsync(orderId);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ContextPassedToBuildMethodThenLoadUsesContainer_DoesNotProduceDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public interface ISqlContainer
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext context) => throw new System.NotImplementedException();
                protected Task<List<TEntity>> LoadListAsync(ISqlContainer container) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task<List<Order>> GetActiveOrdersAsync(IDatabaseContext? context = null)
                {
                    var ctx = context ?? Context;
                    var sc = BuildBaseRetrieve("o", ctx);
                    return await LoadListAsync(sc);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task PublicGatewayMethodThatResolvesCtxButUsesRootContext_ProducesDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<TEntity?> RetrieveOneAsync(TId id, IDatabaseContext context) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task CancelOrderAsync(long orderId, IDatabaseContext? context = null)
                {
                    var ctx = context ?? Context;
                    await RetrieveOneAsync(orderId, Context);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ContextDerivedContainerUsedAsReceiver_DoesNotProduceDiagnostic()
    {
        // sc is built with ctx, then sc.ExecuteNonQueryAsync() should be considered compliant
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public interface ISqlContainer
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected ISqlContainer BuildDelete(TId id, IDatabaseContext context) => throw new System.NotImplementedException();
                protected Task<int> ExecuteNonQueryAsync(ISqlContainer container) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task<int> DeleteOrderAsync(long id, IDatabaseContext? context = null)
                {
                    var ctx = context ?? Context;
                    var sc = BuildDelete(id, ctx);
                    return await ExecuteNonQueryAsync(sc);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task ContextFieldCoalescePattern_DoesNotProduceDiagnostic()
    {
        // var ctx = context ?? _context (field, not property) should also be accepted
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                private IDatabaseContext _context = null!;
                protected Task<TEntity?> RetrieveOneAsync(TId id, IDatabaseContext context) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task<Order?> GetOrderAsync(long id, IDatabaseContext? context = null)
                {
                    var ctx = context ?? _context;
                    return await RetrieveOneAsync(id, ctx);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task ThinDelegationWrapperPassingContextDirectly_DoesNotProduceDiagnostic()
    {
        // public ValueTask<int> CreateAsync(entities, context, ct) => BatchCreateAsync(entities, context, ct);
        var source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context, CancellationToken ct)
                    => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public Task<int> CreateAsync(IReadOnlyList<Order> entities, IDatabaseContext? context = null, CancellationToken ct = default)
                    => BatchCreateAsync(entities, context, ct);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task ContainerCreatedFromCtxReceiverThenExecuted_DoesNotProduceDiagnostic()
    {
        // var sc = ctx.CreateSqlContainer(...) then sc.ExecuteScalarRequiredAsync()
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
                ISqlContainer CreateSqlContainer(string query);
            }

            public interface ISqlContainer
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<T> ExecuteScalarRequiredAsync<T>(ISqlContainer sc) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task<long> GetNextSequenceAsync(IDatabaseContext? context = null)
                {
                    var ctx = context ?? Context;
                    var sc = ctx.CreateSqlContainer("SELECT nextval('seq')");
                    return await ExecuteScalarRequiredAsync<long>(sc);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task PublicGatewayMethodCallingCountAllAsyncWithoutContext_ProducesDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<long> CountAllAsync() => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task<long> GetTotalAsync()
                {
                    return await CountAllAsync();
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task PublicGatewayMethodCallingCountAllAsyncWithResolvedCtx_DoesNotProduceDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            namespace Sample;

            public interface IDatabaseContext
            {
            }

            public class TableGateway<TEntity, TId>
            {
                protected IDatabaseContext Context => throw new System.NotImplementedException();
                protected Task<long> CountAllAsync(IDatabaseContext context) => throw new System.NotImplementedException();
            }

            public sealed class Order
            {
            }

            public sealed class OrderGateway : TableGateway<Order, long>
            {
                public async Task<long> GetTotalAsync(IDatabaseContext? context = null)
                {
                    var ctx = context ?? Context;
                    return await CountAllAsync(ctx);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayMethodContextParameterAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayMethodContextParameterAnalyzer.DiagnosticId,
            expectedCount: 0);
    }
}
