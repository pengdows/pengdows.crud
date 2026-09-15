using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class GatewayCallSiteContextAnalyzerTests
{
    private static readonly Dictionary<string, string> MultiTenancyEnabled = new()
    {
        ["build_property.PengdowsMultiTenancy"] = "true"
    };

    private const string GatewayFixture = """
        using System.Threading.Tasks;

        namespace Sample;

        public interface IDatabaseContext
        {
        }

        public class TableGateway<TEntity, TId>
        {
            protected IDatabaseContext Context => throw new System.NotImplementedException();
            public Task<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null) => throw new System.NotImplementedException();
        }

        public sealed class Order
        {
        }

        public class OrderGateway : TableGateway<Order, long>
        {
        }
        """;

    [Fact]
    public async Task MultiTenancyDisabled_CallSiteWithoutContext_DoesNotProduceDiagnostic()
    {
        var source = GatewayFixture + """

            public static class Caller
            {
                public static Task<bool> Create(OrderGateway gateway, Order order) => gateway.CreateAsync(order);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 0);
        // No globalOptions supplied — the rule must default to off.
    }

    [Fact]
    public async Task MultiTenancyEnabledWithNonTrueValue_DoesNotProduceDiagnostic()
    {
        var source = GatewayFixture + """

            public static class Caller
            {
                public static Task<bool> Create(OrderGateway gateway, Order order) => gateway.CreateAsync(order);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 0,
            globalOptions: new Dictionary<string, string> { ["build_property.PengdowsMultiTenancy"] = "false" });
    }

    [Fact]
    public async Task MultiTenancyEnabled_CallSiteOmittingContext_ProducesDiagnostic()
    {
        var source = GatewayFixture + """

            public static class Caller
            {
                public static Task<bool> Create(OrderGateway gateway, Order order) => gateway.CreateAsync(order);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 1,
            globalOptions: MultiTenancyEnabled);
    }

    [Fact]
    public async Task MultiTenancyEnabled_CallSitePassingExplicitNull_ProducesDiagnostic()
    {
        var source = GatewayFixture + """

            public static class Caller
            {
                public static Task<bool> Create(OrderGateway gateway, Order order) => gateway.CreateAsync(order, null);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 1,
            globalOptions: MultiTenancyEnabled);
    }

    [Fact]
    public async Task MultiTenancyEnabled_CallSitePassingRealContext_DoesNotProduceDiagnostic()
    {
        var source = GatewayFixture + """

            public static class Caller
            {
                public static Task<bool> Create(OrderGateway gateway, Order order, IDatabaseContext tenantContext) =>
                    gateway.CreateAsync(order, tenantContext);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 0,
            globalOptions: MultiTenancyEnabled);
    }

    [Fact]
    public async Task MultiTenancyEnabled_NonGatewayMethodCallWithoutContext_DoesNotProduceDiagnostic()
    {
        var source = GatewayFixture + """

            public static class Helper
            {
                public static Task<bool> CreateAsync(Order order) => Task.FromResult(true);
            }

            public static class Caller
            {
                public static Task<bool> Create(Order order) => Helper.CreateAsync(order);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 0,
            globalOptions: MultiTenancyEnabled);
    }

    [Fact]
    public async Task MultiTenancyEnabled_ThinWrapperPassingNullInternally_ProducesDiagnostic()
    {
        // The single-tenant "convenience overload" pattern PGC025 explicitly allows at the
        // definition level — but under multitenancy enforcement, the internal null it bakes in
        // is exactly the bug this rule exists to catch, so it must still be flagged here.
        var source = GatewayFixture + """

            public sealed class CustomOrderGateway : OrderGateway
            {
                public Task<bool> CreateAsync(Order order) => CreateAsync(order, null);
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 1,
            globalOptions: MultiTenancyEnabled);
    }

    [Fact]
    public async Task MultiTenancyEnabled_GatewayMethodForwardingResolvedContext_DoesNotProduceDiagnostic()
    {
        var source = GatewayFixture + """

            public sealed class CustomOrderGateway : OrderGateway
            {
                public Task<bool> CreateOrderAsync(Order order, IDatabaseContext? context = null)
                {
                    var ctx = context ?? Context;
                    return CreateAsync(order, ctx);
                }
            }
            """;

        await CSharpAnalyzerVerifier<GatewayCallSiteContextAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            GatewayCallSiteContextAnalyzer.DiagnosticId,
            expectedCount: 0,
            globalOptions: MultiTenancyEnabled);
    }
}
