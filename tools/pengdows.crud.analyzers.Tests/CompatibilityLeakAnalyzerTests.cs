using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class CompatibilityLeakAnalyzerTests
{
    [Fact]
    public void Rule_DefaultSeverity_IsError()
    {
        // Deliberately Error: every flagged symbol either does nothing or should never have been
        // public. They stay public only for binary compatibility; application use is rejected.
        var descriptor = Assert.Single(new CompatibilityLeakAnalyzer().SupportedDiagnostics);

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

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

    // DatabaseContext.ReadWriteMode / ProcWrappingStyle are fixed at construction. Their setters are
    // public only so 2.0.5 binaries keep loading, and they do nothing, so an assignment is always a
    // caller bug; reading the properties is fine.
    [Fact]
    public async Task FixedAtConstructionProperties_WritesAreReported_ReadsAreNot()
    {
        var source = """
            namespace pengdows.crud.enums
            {
                public enum ReadWriteMode { ReadOnly, ReadWrite }
                public enum ProcWrappingStyle { None, Exec }
            }

            namespace pengdows.crud
            {
                using pengdows.crud.enums;

                public class DatabaseContext
                {
                    public ReadWriteMode ReadWriteMode { get; set; }
                    public ProcWrappingStyle ProcWrappingStyle { get; set; }
                }
            }

            namespace Consumer
            {
                using pengdows.crud;
                using pengdows.crud.enums;

                public sealed class ConsumerType
                {
                    public bool Reads(DatabaseContext context)
                    {
                        var mode = context.ReadWriteMode;
                        return mode == ReadWriteMode.ReadOnly && context.ProcWrappingStyle == ProcWrappingStyle.Exec;
                    }

                    public void Writes(DatabaseContext context)
                    {
                        context.ReadWriteMode = ReadWriteMode.ReadWrite;
                        context.ProcWrappingStyle = ProcWrappingStyle.None;
                        var created = new DatabaseContext { ReadWriteMode = ReadWriteMode.ReadOnly };
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<CompatibilityLeakAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            CompatibilityLeakAnalyzer.DiagnosticId,
            expectedCount: 3);
    }

    // A using-alias must not sidestep PGC027: the alias directive itself names the blocked type, and
    // an identifier bound through the alias resolves to the aliased type.
    [Fact]
    public async Task AliasedBlockedType_IsReportedAtTheAliasAndAtEachUse()
    {
        var source = """
            using Bad = pengdows.crud.TypeCoercionOptions;

            namespace pengdows.crud
            {
                public sealed class TypeCoercionOptions { }
            }

            namespace Consumer
            {
                public sealed class ConsumerType
                {
                    private Bad? _options;
                    public Bad Create() => new Bad();
                }
            }
            """;

        await CSharpAnalyzerVerifier<CompatibilityLeakAnalyzer>.VerifyDiagnosticCountAsync(
            source,
            CompatibilityLeakAnalyzer.DiagnosticId,
            expectedCount: 4);
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
