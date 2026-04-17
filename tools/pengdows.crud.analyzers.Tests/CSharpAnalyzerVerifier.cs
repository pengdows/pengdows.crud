using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace pengdows.crud.analyzers.Tests;

internal static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        DiagnosticAnalyzer analyzer = new TAnalyzer();
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        return diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToArray();
    }

    public static async Task VerifyDiagnosticCountAsync(string source, string diagnosticId, int expectedCount)
    {
        var diagnostics = await GetDiagnosticsAsync(source);
        var matching = diagnostics.Where(d => d.Id == diagnosticId).ToArray();

        Assert.Equal(expectedCount, matching.Length);
    }

    private static MetadataReference[] GetMetadataReferences()
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
