using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace pengdows.crud.analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSqlPredicateValueAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PGC008";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Parameterize SQL predicate values",
        "Do not inject raw values into SQL predicates or joins; parameterize the value instead",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "WHERE, JOIN ON, HAVING, AND, and OR clauses must not inject raw values directly into SQL text. " +
        "Use AddParameterWithValue(...) and MakeParameterName(...). IS NULL and IS NOT NULL are allowed.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName is not "Append" and not "AppendLine")
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbol == null || !IsSqlQueryBuilder(symbol.ContainingType))
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (!ContainsRawPredicateValue(argument, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, argument.GetLocation()));
    }

    private static bool IsSqlQueryBuilder(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            return false;
        }

        if (typeSymbol.Name is "ISqlQueryBuilder" or "SqlQueryBuilder" or "StringBuilder")
        {
            return true;
        }

        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (iface.Name is "ISqlQueryBuilder" or "SqlQueryBuilder")
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRawPredicateValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case InterpolatedStringExpressionSyntax interpolated:
                return ContainsInterpolatedPredicate(interpolated);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                return ContainsConcatenatedPredicate(binary, semanticModel, cancellationToken);

            default:
                return false;
        }
    }

    private static bool ContainsInterpolatedPredicate(InterpolatedStringExpressionSyntax interpolated)
    {
        if (!interpolated.Contents.OfType<InterpolationSyntax>().Any())
        {
            return false;
        }

        var literalText = string.Concat(
            interpolated.Contents
                .OfType<InterpolatedStringTextSyntax>()
                .Select(static x => x.TextToken.ValueText));

        if (!HasPredicateContext(literalText) || IsNullPredicate(literalText))
        {
            return false;
        }

        // Only flag if at least one hole injects a raw value — method calls (WrapObjectName,
        // MakeParameterName, etc.) are intentional and safe; skip them.
        return interpolated.Contents
            .OfType<InterpolationSyntax>()
            .Any(static hole => hole.Expression is not InvocationExpressionSyntax);
    }

    private static bool ContainsConcatenatedPredicate(
        BinaryExpressionSyntax binary,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var terms = new List<ExpressionSyntax>();
        CollectConcatenationTerms(binary, terms);

        var literalText = string.Concat(terms.Select(term => GetConstantString(term, semanticModel, cancellationToken) ?? string.Empty));
        if (!HasPredicateContext(literalText) || IsNullPredicate(literalText))
        {
            return false;
        }

        // Only flag non-constant terms that are raw values — method calls (WrapObjectName,
        // MakeParameterName, etc.) are intentional and safe; skip them.
        return terms.Any(term =>
            GetConstantString(term, semanticModel, cancellationToken) == null
            && term is not InvocationExpressionSyntax);
    }

    private static void CollectConcatenationTerms(ExpressionSyntax expression, ICollection<ExpressionSyntax> terms)
    {
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            CollectConcatenationTerms(binary.Left, terms);
            CollectConcatenationTerms(binary.Right, terms);
            return;
        }

        terms.Add(expression);
    }

    private static string? GetConstantString(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (!constant.HasValue || constant.Value == null)
        {
            return null;
        }

        return Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
    }

    private static bool HasPredicateContext(string sqlFragment)
    {
        if (string.IsNullOrWhiteSpace(sqlFragment))
        {
            return false;
        }

        var normalized = sqlFragment.ToUpperInvariant();
        if (normalized.Contains(" IS NULL", StringComparison.Ordinal)
            || normalized.Contains(" IS NOT NULL", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Contains(" WHERE ", StringComparison.Ordinal)
               || normalized.Contains(" AND ", StringComparison.Ordinal)
               || normalized.Contains(" OR ", StringComparison.Ordinal)
               || normalized.Contains(" ON ", StringComparison.Ordinal)
               || normalized.Contains(" HAVING ", StringComparison.Ordinal);
    }

    private static bool IsNullPredicate(string sqlFragment)
    {
        var normalized = sqlFragment.ToUpperInvariant();
        return normalized.Contains(" IS NULL", StringComparison.Ordinal)
               || normalized.Contains(" IS NOT NULL", StringComparison.Ordinal);
    }
}
