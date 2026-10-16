using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class DirectStoredProcedureWrapperAnalyzerTests
{
    private const string Scaffold = """
        namespace pengdows.crud;

        public enum ExecutionType { Read, Write }

        public interface ISqlContainer
        {
            string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true, bool captureReturn = false);
            int ExecuteNonQueryAsync(System.Data.CommandType commandType);
        }

        """;

    [Fact]
    public async Task DirectCall_ProducesDiagnostic()
    {
        var source = Scaffold + """
            public sealed class Caller
            {
                public string Build(ISqlContainer container)
                    => container.WrapForStoredProc(ExecutionType.Write);
            }
            """;

        await CSharpAnalyzerVerifier<DirectStoredProcedureWrapperAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DirectStoredProcedureWrapperAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task CommandTypeStoredProcedure_DoesNotProduceDiagnostic()
    {
        var source = Scaffold + """
            public sealed class Caller
            {
                public int Execute(ISqlContainer container)
                    => container.ExecuteNonQueryAsync(System.Data.CommandType.StoredProcedure);
            }
            """;

        await CSharpAnalyzerVerifier<DirectStoredProcedureWrapperAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DirectStoredProcedureWrapperAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task UnrelatedMethodWithSameName_DoesNotProduceDiagnostic()
    {
        var source = """
            namespace Sample;

            public sealed class Container
            {
                public string WrapForStoredProc() => "already wrapped";
            }

            public sealed class Caller
            {
                public string Build(Container container) => container.WrapForStoredProc();
            }
            """;

        await CSharpAnalyzerVerifier<DirectStoredProcedureWrapperAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            DirectStoredProcedureWrapperAnalyzer.DiagnosticId,
            expectedCount: 0);
    }
}
