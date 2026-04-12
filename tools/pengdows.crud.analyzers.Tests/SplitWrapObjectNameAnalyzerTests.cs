using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class SplitWrapObjectNameAnalyzerTests
{
    // Helper: minimal scaffold that exposes WrapObjectName on a context-like class.
    private const string Scaffold = """
        namespace Sample;

        public interface ISqlContainer
        {
            string WrapObjectName(string name);
        }

        """;

    [Fact]
    public async Task BinaryAdd_ProducesDiagnostic()
    {
        var source = Scaffold + """
            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => sc.WrapObjectName("alias") + "." + sc.WrapObjectName("column");
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task BinaryAdd_UnqualifiedMethodName_ProducesDiagnostic()
    {
        // WrapObjectName called without receiver (inside a gateway subclass)
        var source = """
            namespace Sample;

            public class SqlBase
            {
                protected string WrapObjectName(string name) => name;
            }

            public sealed class Gateway : SqlBase
            {
                public string Build()
                    => WrapObjectName("alias") + "." + WrapObjectName("column");
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task Interpolated_ProducesDiagnostic()
    {
        var source = Scaffold + """
            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => $"SELECT {sc.WrapObjectName("alias")}.{sc.WrapObjectName("column")} FROM t";
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task AlreadyCombined_DoesNotProduceDiagnostic()
    {
        var source = Scaffold + """
            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => sc.WrapObjectName("alias.column");
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task LeftArgAlreadyHasDot_DoesNotProduceDiagnostic()
    {
        // Left part already has a dot — split was intentional (schema.alias)
        var source = Scaffold + """
            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => sc.WrapObjectName("schema.alias") + "." + sc.WrapObjectName("column");
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task NonLiteralArguments_DoesNotProduceDiagnostic()
    {
        // Arguments are identifiers, not string literals — analyzer can't combine them
        var source = Scaffold + """
            public sealed class Gateway
            {
                public string Build(ISqlContainer sc, string alias, string column)
                    => sc.WrapObjectName(alias) + "." + sc.WrapObjectName(column);
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task DifferentMethodName_DoesNotProduceDiagnostic()
    {
        var source = """
            namespace Sample;

            public interface ISqlContainer
            {
                string QuoteIdentifier(string name);
            }

            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => sc.QuoteIdentifier("alias") + "." + sc.QuoteIdentifier("column");
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task ChainOfThreeParts_ProducesOneDiagnostic()
    {
        // a + "." + b + "." + c
        // AST: ((a + ".") + b) + "." + c  — outer covers (a+".")+b and c
        // The outer node's right is WrapObjectName("c"); outer's left is ((a+"."+b))
        // Since inner.Left is not a simple WrapObjectName call, only outer is tested
        // Analyzer skips outer because parent is an add (to avoid double-report).
        // The inner ((a+".") + b) IS the one that gets reported.
        var source = Scaffold + """
            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => sc.WrapObjectName("a") + "." + sc.WrapObjectName("b") + "." + sc.WrapObjectName("c");
            }
            """;

        await CSharpAnalyzerVerifier<SplitWrapObjectNameAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            SplitWrapObjectNameAnalyzer.DiagnosticId,
            expectedCount: 1);
    }
}
