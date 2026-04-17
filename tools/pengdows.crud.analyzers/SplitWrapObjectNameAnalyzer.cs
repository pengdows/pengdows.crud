using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace pengdows.crud.analyzers;

/// <summary>
/// Flags the split two-call pattern <c>WrapObjectName("alias") + "." + WrapObjectName("column")</c>
/// and suggests the single-call form <c>WrapObjectName("alias.column")</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SplitWrapObjectNameAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PGC026";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Use WrapObjectName(\"alias.column\") for qualified names",
        "Use WrapObjectName(\"{0}.{1}\") instead of two separate WrapObjectName calls joined by \".\"",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "WrapObjectName(\"alias.column\") handles the qualified name in one call, producing correct dialect-specific " +
        "quoting. Splitting into WrapObjectName(\"alias\") + \".\" + WrapObjectName(\"column\") risks incorrect quoting " +
        "if either part contains a reserved word, and is harder to read.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInterpolated, SyntaxKind.InterpolatedStringExpression);
        context.RegisterSyntaxNodeAction(AnalyzeBinaryAdd, SyntaxKind.AddExpression);
    }

    // -------------------------------------------------------------------------
    // Interpolated string: $"...{x.WrapObjectName("a")}.{x.WrapObjectName("b")}..."
    // -------------------------------------------------------------------------

    private static void AnalyzeInterpolated(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InterpolatedStringExpressionSyntax interpolated)
        {
            return;
        }

        var contents = interpolated.Contents;
        for (var i = 0; i < contents.Count - 2; i++)
        {
            // Element i   : interpolation hole containing WrapObjectName("X") where X has no dot
            // Element i+1 : literal text exactly "."
            // Element i+2 : interpolation hole containing WrapObjectName("Y") where Y has no dot

            if (contents[i] is not InterpolationSyntax leftHole ||
                contents[i + 1] is not InterpolatedStringTextSyntax dot ||
                contents[i + 2] is not InterpolationSyntax rightHole)
            {
                continue;
            }

            if (dot.TextToken.ValueText != ".")
            {
                continue;
            }

            if (!TryGetWrapObjectNameArgument(leftHole.Expression, out var leftArg) ||
                !TryGetWrapObjectNameArgument(rightHole.Expression, out var rightArg))
            {
                continue;
            }

            // Only flag simple parts — if either already contains a dot, the split was intentional
            if (leftArg.Contains('.') || rightArg.Contains('.'))
            {
                continue;
            }

            var span = contents[i].Span.Start;
            var end = contents[i + 2].Span.End;
            var location = Location.Create(
                interpolated.SyntaxTree,
                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(span, end));

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, leftArg, rightArg));
            i += 2; // skip the matched triple
        }
    }

    // -------------------------------------------------------------------------
    // Binary add: WrapObjectName("a") + "." + WrapObjectName("b")
    // The AST is left-associative: ((WrapObjectName("a") + ".") + WrapObjectName("b"))
    // -------------------------------------------------------------------------

    private static void AnalyzeBinaryAdd(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not BinaryExpressionSyntax outer ||
            !outer.IsKind(SyntaxKind.AddExpression))
        {
            return;
        }

        // outer.Right must be WrapObjectName("b")
        if (!TryGetWrapObjectNameArgument(outer.Right, out var rightArg))
        {
            return;
        }

        // outer.Left must be (WrapObjectName("a") + ".")
        if (outer.Left is not BinaryExpressionSyntax inner ||
            !inner.IsKind(SyntaxKind.AddExpression))
        {
            return;
        }

        if (!TryGetWrapObjectNameArgument(inner.Left, out var leftArg))
        {
            return;
        }

        if (!IsLiteralDot(inner.Right))
        {
            return;
        }

        if (leftArg.Contains('.') || rightArg.Contains('.'))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, outer.GetLocation(), leftArg, rightArg));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true and extracts the string literal argument if the expression is a
    /// <c>WrapObjectName("literal")</c> invocation.
    /// </summary>
    private static bool TryGetWrapObjectNameArgument(ExpressionSyntax expression, out string argument)
    {
        argument = string.Empty;

        if (expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null
        };

        if (methodName != "WrapObjectName")
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return false;
        }

        argument = literal.Token.ValueText;
        return true;
    }

    private static bool IsLiteralDot(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.StringLiteralExpression) &&
               literal.Token.ValueText == ".";
    }
}
