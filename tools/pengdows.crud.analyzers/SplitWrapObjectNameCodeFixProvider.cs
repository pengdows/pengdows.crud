using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace pengdows.crud.analyzers;

/// <summary>
/// Code fix for <see cref="SplitWrapObjectNameAnalyzer"/> (PGC026).
/// Replaces <c>WrapObjectName("a") + "." + WrapObjectName("b")</c> with
/// <c>WrapObjectName("a.b")</c>, and the analogous interpolated-string pattern.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SplitWrapObjectNameCodeFixProvider))]
[Shared]
public sealed class SplitWrapObjectNameCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SplitWrapObjectNameAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (root == null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var span = diagnostic.Location.SourceSpan;
        var node = root.FindNode(span, getInnermostNodeForTie: true);

        // Try binary add first, then interpolated string triple.
        if (TryGetBinaryAddFix(node, out var binaryNode, out var combinedArg))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Use WrapObjectName(\"{combinedArg}\")",
                    createChangedDocument: ct => ApplyBinaryFixAsync(context.Document, root, binaryNode!, combinedArg!, ct),
                    equivalenceKey: "PGC026_BinaryFix"),
                diagnostic);
            return;
        }

        if (TryGetInterpolatedFix(node, out var leftHole, out var rightHole, out combinedArg))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Use WrapObjectName(\"{combinedArg}\")",
                    createChangedDocument: ct => ApplyInterpolatedFixAsync(context.Document, root, leftHole!, rightHole!, combinedArg!, ct),
                    equivalenceKey: "PGC026_InterpolatedFix"),
                diagnostic);
        }
    }

    // -------------------------------------------------------------------------
    // Binary add fix
    // -------------------------------------------------------------------------

    private static bool TryGetBinaryAddFix(
        SyntaxNode node,
        out BinaryExpressionSyntax? outerNode,
        out string? combinedArg)
    {
        outerNode = null;
        combinedArg = null;

        // Walk up to find the binary add node that matches the pattern.
        var candidate = node.AncestorsAndSelf()
            .OfType<BinaryExpressionSyntax>()
            .FirstOrDefault(n => IsSplitBinaryAdd(n, out _, out _));

        if (candidate == null)
        {
            return false;
        }

        IsSplitBinaryAdd(candidate, out var leftArg, out var rightArg);
        outerNode = candidate;
        combinedArg = $"{leftArg}.{rightArg}";
        return true;
    }

    private static bool IsSplitBinaryAdd(
        BinaryExpressionSyntax outer,
        out string leftArg,
        out string rightArg)
    {
        leftArg = string.Empty;
        rightArg = string.Empty;

        if (!outer.IsKind(SyntaxKind.AddExpression))
        {
            return false;
        }

        if (!TryGetWrapObjectNameArgument(outer.Right, out rightArg))
        {
            return false;
        }

        if (outer.Left is not BinaryExpressionSyntax inner || !inner.IsKind(SyntaxKind.AddExpression))
        {
            return false;
        }

        if (!TryGetWrapObjectNameArgument(inner.Left, out leftArg))
        {
            return false;
        }

        if (!IsLiteralDot(inner.Right))
        {
            return false;
        }

        return !leftArg.Contains('.') && !rightArg.Contains('.');
    }

    private static Task<Document> ApplyBinaryFixAsync(
        Document document,
        SyntaxNode root,
        BinaryExpressionSyntax outer,
        string combinedArg,
        CancellationToken _)
    {
        // Build replacement: preserve receiver if present (sc.WrapObjectName or just WrapObjectName).
        var inner = (BinaryExpressionSyntax)outer.Left;
        var leftInvocation = (InvocationExpressionSyntax)inner.Left;

        var newArgList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(combinedArg)))));

        var replacement = leftInvocation.WithArgumentList(newArgList);
        var newRoot = root.ReplaceNode(outer, replacement);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    // -------------------------------------------------------------------------
    // Interpolated string fix
    // -------------------------------------------------------------------------

    private static bool TryGetInterpolatedFix(
        SyntaxNode node,
        out InterpolationSyntax? leftHole,
        out InterpolationSyntax? rightHole,
        out string? combinedArg)
    {
        leftHole = null;
        rightHole = null;
        combinedArg = null;

        // The diagnostic span covers the left hole through the right hole.
        // Walk up to find the interpolated string.
        var interpolated = node.AncestorsAndSelf()
            .OfType<InterpolatedStringExpressionSyntax>()
            .FirstOrDefault();

        if (interpolated == null)
        {
            return false;
        }

        var contents = interpolated.Contents;
        for (var i = 0; i < contents.Count - 2; i++)
        {
            if (contents[i] is not InterpolationSyntax left ||
                contents[i + 1] is not InterpolatedStringTextSyntax dot ||
                contents[i + 2] is not InterpolationSyntax right)
            {
                continue;
            }

            if (dot.TextToken.ValueText != ".")
            {
                continue;
            }

            if (!TryGetWrapObjectNameArgument(left.Expression, out var la) ||
                !TryGetWrapObjectNameArgument(right.Expression, out var ra))
            {
                continue;
            }

            if (la.Contains('.') || ra.Contains('.'))
            {
                continue;
            }

            // Check that the span overlaps this triple.
            if (contents[i].Span.IntersectsWith(node.Span) ||
                contents[i + 2].Span.IntersectsWith(node.Span))
            {
                leftHole = left;
                rightHole = right;
                combinedArg = $"{la}.{ra}";
                return true;
            }
        }

        return false;
    }

    private static Task<Document> ApplyInterpolatedFixAsync(
        Document document,
        SyntaxNode root,
        InterpolationSyntax leftHole,
        InterpolationSyntax rightHole,
        string combinedArg,
        CancellationToken _)
    {
        // Find the interpolated string that owns these holes.
        var interpolated = (InterpolatedStringExpressionSyntax)leftHole.Parent!;
        var contents = interpolated.Contents;

        var leftIndex = contents.IndexOf(leftHole);
        var dotIndex = leftIndex + 1;
        var rightIndex = leftIndex + 2;

        // Build the combined invocation: preserve receiver from the left hole's expression.
        var leftInvocation = (InvocationExpressionSyntax)leftHole.Expression;
        var newArgList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(combinedArg)))));
        var combinedInvocation = leftInvocation.WithArgumentList(newArgList);
        var combinedHole = SyntaxFactory.Interpolation(combinedInvocation);

        // Build new contents list: replace [left, dot, right] with [combined].
        var newContents = new List<InterpolatedStringContentSyntax>();
        for (var i = 0; i < contents.Count; i++)
        {
            if (i == leftIndex)
            {
                newContents.Add(combinedHole);
            }
            else if (i == dotIndex || i == rightIndex)
            {
                // skip dot text and right hole
            }
            else
            {
                newContents.Add(contents[i]);
            }
        }

        var newInterpolated = interpolated.WithContents(
            SyntaxFactory.List(newContents));

        var newRoot = root.ReplaceNode(interpolated, newInterpolated);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    // -------------------------------------------------------------------------
    // Shared helpers (duplicated from analyzer to keep projects independent)
    // -------------------------------------------------------------------------

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

    private static bool IsLiteralDot(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal &&
        literal.IsKind(SyntaxKind.StringLiteralExpression) &&
        literal.Token.ValueText == ".";
}
