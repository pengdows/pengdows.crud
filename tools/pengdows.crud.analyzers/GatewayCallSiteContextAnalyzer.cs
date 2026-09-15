using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace pengdows.crud.analyzers;

/// <summary>
/// Call-site companion to PGC025 (<see cref="GatewayMethodContextParameterAnalyzer"/>). PGC025
/// protects the callee: a gateway method that executes database work must resolve and forward
/// its own context parameter. This rule protects the caller: once a consuming project opts in to
/// multitenancy enforcement, any call to a gateway execution method must supply a non-null
/// execution context — the implicit "fall back to this gateway's constructor-time default
/// context" behavior that is perfectly valid for a single-tenant application becomes a compile
/// error, because in a multitenant application that fallback silently selects the wrong tenant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately opt-in, not inferred.</b> Whether an application uses multitenancy is not
/// something this analyzer heuristically guesses (e.g. by searching the compilation for a call to
/// some <c>AddMultiTenancy(...)</c> registration method) — that would produce false negatives for
/// any application that wires up <c>ITenantContextRegistry</c> a different way, or across
/// assembly boundaries. Instead, enforcement is gated behind an explicit MSBuild property,
/// <c>PengdowsMultiTenancy</c>, exposed to analyzers as the <c>build_property.PengdowsMultiTenancy</c>
/// <see cref="AnalyzerConfigOptions"/> key (see <c>build/pengdows.crud.analyzers.props</c>). When
/// unset or not exactly <c>"true"</c> (case-insensitive), this analyzer reports nothing at all —
/// a single-tenant application's existing no-context convenience calls remain completely valid.
/// </para>
/// <para>
/// <b>What counts as "no context supplied."</b> Uses the <see cref="IOperation"/> API
/// (<see cref="IInvocationOperation"/>) rather than raw syntax, so every parameter — including
/// ones the caller omitted — has a corresponding <see cref="IArgumentOperation"/>: an omitted
/// optional context parameter surfaces as <see cref="ArgumentKind.DefaultValue"/>. A violation is
/// either that (the caller relied on the default), or an argument that <i>is</i> present but is a
/// literal <c>null</c>/<c>default</c> constant. A non-constant expression (a variable, a member
/// access, a ternary, a null-coalescing fallback) is always accepted — this analyzer proves
/// "a real value was supplied," not "that value is provably non-null at runtime," the same
/// pragmatic bar PGC025 already sets.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GatewayCallSiteContextAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PGC027";
    internal const string MultiTenancyPropertyKey = "build_property.PengdowsMultiTenancy";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Gateway call sites must supply an execution context under multitenancy enforcement",
        "This call to a gateway execution method must supply a non-null IDatabaseContext/" +
        "ITransactionContext argument — relying on the gateway's default context is not valid " +
        "once multitenancy enforcement (PengdowsMultiTenancy=true) is enabled",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "Under opt-in multitenancy enforcement, every call to a gateway execution/build method " +
        "must explicitly select which tenant's IDatabaseContext (or ITransactionContext) to run " +
        "against. Omitting the argument, or passing a literal null/default, silently falls back " +
        "to the gateway's own constructor-time default context — valid for a single-tenant " +
        "application, but a cross-tenant correctness bug once multitenancy is in play.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (!IsMultiTenancyEnforcementEnabled(compilationContext.Options))
            {
                return;
            }

            compilationContext.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static bool IsMultiTenancyEnforcementEnabled(AnalyzerOptions options)
    {
        return options.AnalyzerConfigOptionsProvider.GlobalOptions
                   .TryGetValue(MultiTenancyPropertyKey, out var value)
               && string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        if (context.Operation is not IInvocationOperation invocation)
        {
            return;
        }

        var targetMethod = invocation.TargetMethod;
        if (!GatewayAnalysisHelpers.ExecutionMethodNames.Contains(targetMethod.Name))
        {
            return;
        }

        if (!GatewayAnalysisHelpers.IsGatewayType(targetMethod.ContainingType))
        {
            return;
        }

        var contextParameter = targetMethod.Parameters.FirstOrDefault(
            p => GatewayAnalysisHelpers.IsContextType(p.Type));
        if (contextParameter is null)
        {
            // This overload has no context parameter to supply at all — nothing for this rule to
            // check (e.g. a container-based Load* overload, exempted the same way PGC025 exempts
            // ISqlContainer-parameter methods).
            return;
        }

        var argument = invocation.Arguments.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.Parameter, contextParameter));
        if (argument is null)
        {
            return;
        }

        var isMissing = argument.ArgumentKind == ArgumentKind.DefaultValue
                         || IsNullOrDefaultConstant(argument.Value);

        if (isMissing)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
        }
    }

    /// <summary>
    /// True for a literal <c>null</c> or <c>default</c>/<c>default(T)</c> argument value. A
    /// <c>null</c> literal converted to a nullable/interface parameter type is represented as an
    /// <see cref="IConversionOperation"/> wrapping the literal — the conversion node itself does
    /// not carry the inner literal's <see cref="IOperation.ConstantValue"/>, so this unwraps
    /// through any chain of conversions before checking.
    /// </summary>
    private static bool IsNullOrDefaultConstant(IOperation value)
    {
        var current = value;
        while (current is IConversionOperation conversion)
        {
            current = conversion.Operand;
        }

        return current is IDefaultValueOperation || current.ConstantValue is { HasValue: true, Value: null };
    }
}
