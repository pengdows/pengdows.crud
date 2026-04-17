using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Xunit;

namespace pengdows.crud.analyzers.Tests;

internal static class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    /// <summary>
    /// Verifies that applying the code fix to <paramref name="source"/> produces
    /// output that matches <paramref name="expectedSource"/> after normalization.
    /// </summary>
    public static async Task VerifyFixAsync(string source, string expectedSource)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "FixTests", "FixTests", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectParseOptions(projectId,
                new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(projectId, GetMetadataReferences())
            .AddDocument(documentId, "Test.cs", source);

        var project = solution.GetProject(projectId)!;
        var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
        Assert.NotNull(compilation);

        DiagnosticAnalyzer analyzer = new TAnalyzer();
        var diagnostics = (await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false))
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToArray();

        Assert.NotEmpty(diagnostics);

        // Apply the first available code fix action.
        var document = solution.GetDocument(documentId)!;
        var codeFix = new TCodeFix();
        CodeAction? fixAction = null;

        var context = new CodeFixContext(
            document,
            diagnostics[0],
            (action, _) => fixAction = action,
            CancellationToken.None);

        await codeFix.RegisterCodeFixesAsync(context).ConfigureAwait(false);
        Assert.NotNull(fixAction);

        var operations = await fixAction!.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applyOp = operations.OfType<ApplyChangesOperation>().Single();
        var changedDoc = applyOp.ChangedSolution.GetDocument(documentId)!;
        var formattedDoc = await Formatter.FormatAsync(changedDoc).ConfigureAwait(false);
        var actualText = (await formattedDoc.GetTextAsync().ConfigureAwait(false)).ToString();

        Assert.Equal(Normalize(expectedSource), Normalize(actualText));
    }

    private static string Normalize(string s) =>
        s.Replace("\r\n", "\n").Trim();

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        return
        [
            MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DbType).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location)
        ];
    }
}
