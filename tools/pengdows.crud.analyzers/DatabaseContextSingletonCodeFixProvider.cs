using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace pengdows.crud.analyzers;

/// <summary>
/// Code fix for <see cref="DatabaseContextSingletonAnalyzer"/> (PGC001).
/// Replaces <c>AddScoped</c> or <c>AddTransient</c> with <c>AddSingleton</c>,
/// preserving all type arguments and call arguments unchanged.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DatabaseContextSingletonCodeFixProvider))]
[Shared]
public sealed class DatabaseContextSingletonCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DatabaseContextSingletonAnalyzer.DiagnosticId);

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

        // The diagnostic is reported on memberAccess.Name — the method name token.
        var nameNode = root.FindNode(span, getInnermostNodeForTie: true);

        // nameNode may be a GenericNameSyntax (AddScoped<T>) or SimpleNameSyntax (AddScoped).
        // We replace its identifier token.
        SyntaxNode? replacement = nameNode switch
        {
            GenericNameSyntax generic => generic.WithIdentifier(
                SyntaxFactory.Identifier("AddSingleton")
                    .WithTriviaFrom(generic.Identifier)),
            SimpleNameSyntax simple => simple.WithIdentifier(
                SyntaxFactory.Identifier("AddSingleton")
                    .WithTriviaFrom(simple.Identifier)),
            _ => null
        };

        if (replacement == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Change to AddSingleton",
                createChangedDocument: ct =>
                {
                    var newRoot = root.ReplaceNode(nameNode, replacement);
                    return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                },
                equivalenceKey: "PGC001_SingletonFix"),
            diagnostic);
    }
}
