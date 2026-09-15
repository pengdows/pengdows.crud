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
    public static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        DiagnosticAnalyzer analyzer = new TAnalyzer();
        var analyzerOptions = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(globalOptions ?? new Dictionary<string, string>()));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer), analyzerOptions)
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        return diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToArray();
    }

    public static async Task VerifyDiagnosticCountAsync(
        string source,
        string diagnosticId,
        int expectedCount,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var diagnostics = await GetDiagnosticsAsync(source, globalOptions);
        var matching = diagnostics.Where(d => d.Id == diagnosticId).ToArray();

        Assert.Equal(expectedCount, matching.Length);
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
        {
            GlobalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value) =>
            _values.TryGetValue(key, out value!);
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
