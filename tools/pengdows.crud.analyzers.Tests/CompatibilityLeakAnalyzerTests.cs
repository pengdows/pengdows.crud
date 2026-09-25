using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class CompatibilityLeakAnalyzerTests
{
    [Fact]
    public async Task DataSourcePropertyReference_ProducesDiagnostic()
    {
        var source = """
            using System.Data.Common;

            namespace pengdows.crud;

            public interface IDatabaseContext
            {
                DbDataSource? DataSource { get; }
            }

            public sealed class Consumer
            {
                public DbDataSource? Get(IDatabaseContext context) => context.DataSource;
            }
            """;

        await CSharpAnalyzerVerifier<CompatibilityLeakAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            CompatibilityLeakAnalyzer.DiagnosticId,
            expectedCount: 1);
    }

    [Fact]
    public async Task InternalImplementationReference_DoesNotProduceDiagnostic()
    {
        var source = """
            using System.Data.Common;

            namespace pengdows.crud;

            public interface IDatabaseContext
            {
                DbDataSource? DataSource { get; }
            }

            public sealed class DatabaseContext : IDatabaseContext
            {
                public DbDataSource? DataSource => null;
            }
            """;

        await CSharpAnalyzerVerifier<CompatibilityLeakAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            CompatibilityLeakAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task LegacyTypesAndAttributes_ProduceOneDiagnosticPerUse()
    {
        var source = """
            using pengdows.crud;
            using pengdows.crud.connection;
            using pengdows.crud.threading;
            using pengdows.crud.types.attributes;

            namespace pengdows.crud
            {
                public sealed class TypeCoercionOptions { }
                public sealed class EphemeralSecureString { }
            }

            namespace pengdows.crud.connection
            {
                public interface IConnectionLocalState { }
            }

            namespace pengdows.crud.threading
            {
                public interface ILockerAsync { }
            }

            namespace pengdows.crud.types.attributes
            {
                public enum EnumStorage { Name }
                public sealed class CurrencyAttribute : System.Attribute
                {
                    public CurrencyAttribute(string code) { }
                }
            }

            namespace Consumer
            {
                [Currency("USD")]
                public sealed class Entity
                {
                    public EnumStorage Storage { get; set; }
                }

                public sealed class ConsumerType
                {
                    public TypeCoercionOptions Options { get; }
                    public ILockerAsync Locker { get; }
                    public IConnectionLocalState State { get; }
                    public EphemeralSecureString Secure { get; }
                }
            }
            """;

        await CSharpAnalyzerVerifier<CompatibilityLeakAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            CompatibilityLeakAnalyzer.DiagnosticId,
            expectedCount: 6);
    }

    [Fact]
    public async Task UnrelatedTypeWithSameShortName_DoesNotProduceDiagnostic()
    {
        var source = """
            namespace Consumer;

            public sealed class TypeCoercionOptions { }

            public sealed class ConsumerType
            {
                public TypeCoercionOptions Options { get; } = new();
            }
            """;

        await CSharpAnalyzerVerifier<CompatibilityLeakAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            CompatibilityLeakAnalyzer.DiagnosticId,
            expectedCount: 0);
    }

    [Fact]
    public async Task SqlStandardHeuristicReferences_ProduceDiagnostics()
    {
        var source = """
            namespace pengdows.crud.enums
            {
                public enum SqlStandardLevel { Sql92 }
            }

            namespace pengdows.crud.dialects
            {
                public interface ISqlDialect
                {
                    pengdows.crud.enums.SqlStandardLevel MaxSupportedStandard { get; }
                }
            }

            namespace Consumer
            {
                public sealed class ConsumerType
                {
                    public pengdows.crud.enums.SqlStandardLevel Level { get; }

                    public pengdows.crud.enums.SqlStandardLevel Read(pengdows.crud.dialects.ISqlDialect dialect)
                    {
                        return dialect.MaxSupportedStandard;
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<CompatibilityLeakAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            CompatibilityLeakAnalyzer.DiagnosticId,
            expectedCount: 4);
    }
}
