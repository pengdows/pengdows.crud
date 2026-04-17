using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class SplitWrapObjectNameCodeFixProviderTests
{
    [Fact]
    public async Task BinaryAdd_FixReplacesWithCombinedCall()
    {
        var source = """
            namespace Sample;

            public interface ISqlContainer
            {
                string WrapObjectName(string name);
            }

            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => sc.WrapObjectName("alias") + "." + sc.WrapObjectName("column");
            }
            """;

        var expected = """
            namespace Sample;

            public interface ISqlContainer
            {
                string WrapObjectName(string name);
            }

            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => sc.WrapObjectName("alias.column");
            }
            """;

        await CSharpCodeFixVerifier<SplitWrapObjectNameAnalyzer, SplitWrapObjectNameCodeFixProvider>
            .VerifyFixAsync(source, expected);
    }

    [Fact]
    public async Task BinaryAdd_UnqualifiedCall_FixReplacesWithCombinedCall()
    {
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

        var expected = """
            namespace Sample;

            public class SqlBase
            {
                protected string WrapObjectName(string name) => name;
            }

            public sealed class Gateway : SqlBase
            {
                public string Build()
                    => WrapObjectName("alias.column");
            }
            """;

        await CSharpCodeFixVerifier<SplitWrapObjectNameAnalyzer, SplitWrapObjectNameCodeFixProvider>
            .VerifyFixAsync(source, expected);
    }

    [Fact]
    public async Task Interpolated_FixReplacesWithCombinedHole()
    {
        var source = """
            namespace Sample;

            public interface ISqlContainer
            {
                string WrapObjectName(string name);
            }

            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => $"SELECT {sc.WrapObjectName("alias")}.{sc.WrapObjectName("column")} FROM t";
            }
            """;

        var expected = """
            namespace Sample;

            public interface ISqlContainer
            {
                string WrapObjectName(string name);
            }

            public sealed class Gateway
            {
                public string Build(ISqlContainer sc)
                    => $"SELECT {sc.WrapObjectName("alias.column")} FROM t";
            }
            """;

        await CSharpCodeFixVerifier<SplitWrapObjectNameAnalyzer, SplitWrapObjectNameCodeFixProvider>
            .VerifyFixAsync(source, expected);
    }
}
