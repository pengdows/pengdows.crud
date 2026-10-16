using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace pengdows.crud.analyzers;

/// <summary>
/// Prevents application code from bypassing the stored-procedure execution path by calling
/// the legacy SQL-wrapping helper directly.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectStoredProcedureWrapperAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PGC030";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Do not call WrapForStoredProc directly",
        "Do not call WrapForStoredProc directly; execute the container with CommandType.StoredProcedure",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "WrapForStoredProc is an implementation detail. Use an ExecuteScalarAsync or ExecuteNonQueryAsync overload with CommandType.StoredProcedure so the normal execution and lifecycle path is used.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        if (string.Equals(context.Compilation.AssemblyName, "pengdows.crud", StringComparison.Ordinal))
        {
            return;
        }

        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!string.Equals(method.Name, "WrapForStoredProc", StringComparison.Ordinal)
            || !IsPengdowsStoredProcedureWrapper(method))
        {
            return;
        }

        // captureReturn is only reachable through the direct call (CommandType.StoredProcedure never captures the
        // return value) and is documented in docs/stored-procedures.md, so asking for it is not a bypass.
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "captureReturn" && !argument.IsImplicit
                && !(argument.Value.ConstantValue is { HasValue: true, Value: false }))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
    }

    private static bool IsPengdowsStoredProcedureWrapper(IMethodSymbol method)
    {
        var containingType = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);

        return containingType is "pengdows.crud.ISqlContainer" or "pengdows.crud.SqlContainer";
    }
}
