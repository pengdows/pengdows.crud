using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class RawSqlPredicateValueAnalyzerTests
{
    [Fact]
    public async Task InterpolatedWhereClauseWithRawValue_ProducesDiagnostic()
    {
        var source = """
            using System;

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
                public void Build(ISqlContainer sc, int id)
                {
                    sc.Query.Append($" WHERE t.id = {id}");
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ConcatenatedJoinPredicateWithRawValue_ProducesDiagnostic()
    {
        var source = """
            using System;

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
                public void Build(ISqlContainer sc, int customerId)
                {
                    sc.Query.Append(" INNER JOIN customers c ON c.id = " + customerId);
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task ParameterizedPredicate_DoesNotProduceDiagnostic()
    {
        var source = """
            using System.Data;

            namespace Sample;

            public interface IDbParameter
            {
            }

            public interface ISqlQueryBuilder
            {
                void Append(string value);
            }

            public interface ISqlContainer
            {
                ISqlQueryBuilder Query { get; }
                IDbParameter AddParameterWithValue(string? name, DbType type, object value);
                string MakeParameterName(IDbParameter parameter);
            }

            public sealed class Gateway
            {
                public void Build(ISqlContainer sc, int id)
                {
                    sc.Query.Append(" WHERE t.id = ");
                    var p = sc.AddParameterWithValue("id", DbType.Int32, id);
                    sc.Query.Append(sc.MakeParameterName(p));
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task InterpolatedWhereWithWrapObjectNameAndMakeParameterName_DoesNotProduceDiagnostic()
    {
        var source = """
            namespace Sample;

            public interface ISqlQueryBuilder
            {
                void Append(string value);
            }

            public interface IDbParameter
            {
            }

            public interface ISqlContainer
            {
                ISqlQueryBuilder Query { get; }
                string WrapObjectName(string name);
                string MakeParameterName(IDbParameter parameter);
            }

            public sealed class Gateway
            {
                public void Build(ISqlContainer sc, IDbParameter parameter)
                {
                    sc.Query.Append($" WHERE {sc.WrapObjectName("u")}.{sc.WrapObjectName("username")} = {sc.MakeParameterName(parameter)}");
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task ConcatenatedWhereWithMakeParameterName_DoesNotProduceDiagnostic()
    {
        var source = """
            namespace Sample;

            public interface ISqlQueryBuilder
            {
                void Append(string value);
            }

            public interface IDbParameter
            {
            }

            public interface ISqlContainer
            {
                ISqlQueryBuilder Query { get; }
                string WrapObjectName(string name);
                string MakeParameterName(IDbParameter parameter);
            }

            public sealed class Gateway
            {
                public void Build(ISqlContainer sc, IDbParameter p)
                {
                    sc.Query.Append(" WHERE " + sc.WrapObjectName("u.username") + " = " + sc.MakeParameterName(p));
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task IsNullPredicate_DoesNotProduceDiagnostic()
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
                public void Build(ISqlContainer sc)
                {
                    sc.Query.Append(" WHERE deleted_at IS NULL");
                    sc.Query.Append(" AND archived_at IS NOT NULL");
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task RawStringBuilderAppendWithInterpolatedWhere_ProducesDiagnostic()
    {
        var source = """
            using System.Text;

            namespace Sample;

            public sealed class SqlBuilder
            {
                public void Build(int id)
                {
                    var sb = new StringBuilder();
                    sb.Append($" WHERE t.id = {id}");
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task RawStringBuilderAppendWithConstantWhereOnly_DoesNotProduceDiagnostic()
    {
        var source = """
            using System.Text;

            namespace Sample;

            public sealed class SqlBuilder
            {
                public void Build()
                {
                    var sb = new StringBuilder();
                    sb.Append(" WHERE deleted_at IS NULL");
                }
            }
            """;

        await CSharpAnalyzerVerifier<RawSqlPredicateValueAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            RawSqlPredicateValueAnalyzer.DiagnosticId,
            expectedCount: 0);
    }
}
