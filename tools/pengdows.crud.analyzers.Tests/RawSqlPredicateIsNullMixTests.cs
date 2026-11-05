using Xunit;

namespace pengdows.crud.analyzers.Tests;

// IS NULL / IS NOT NULL carry no value, but a raw value in the same predicate is still injected.
// The analyzer must judge each raw hole, not suppress the whole fragment because it mentions IS NULL.
public sealed class RawSqlPredicateIsNullMixTests
{
    [Fact]
    public async Task InterpolatedRawValueAlongsideIsNull_ProducesDiagnostic()
    {
        var source = """
            namespace Sample;

            public interface ISqlQueryBuilder
            {
                void Append(string value);
            }

            public interface ISqlContainer
            {
                ISqlQueryBuilder Query { get; }
            }

            public sealed class Gateway
            {
                public void Build(ISqlContainer sc, int b)
                {
                    sc.Query.Append($" WHERE a IS NULL AND b = {b}");
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ConcatenatedRawValueAlongsideIsNotNull_ProducesDiagnostic()
    {
        var source = """
            namespace Sample;

            public interface ISqlQueryBuilder
            {
                void Append(string value);
            }

            public interface ISqlContainer
            {
                ISqlQueryBuilder Query { get; }
            }

            public sealed class Gateway
            {
                public void Build(ISqlContainer sc, string name)
                {
                    sc.Query.Append(" WHERE a IS NOT NULL AND name = " + name);
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task IsNullAlongsideParameterizedValue_DoesNotProduceDiagnostic()
    {
        var source = """
            namespace Sample;

            public interface ISqlQueryBuilder
            {
                void Append(string value);
            }

            public interface ISqlContainer
            {
                ISqlQueryBuilder Query { get; }
                string MakeParameterName(string name);
            }

            public sealed class Gateway
            {
                public void Build(ISqlContainer sc)
                {
                    sc.Query.Append($" WHERE a IS NULL AND b = {sc.MakeParameterName("b0")}");
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 0);
    }
}
