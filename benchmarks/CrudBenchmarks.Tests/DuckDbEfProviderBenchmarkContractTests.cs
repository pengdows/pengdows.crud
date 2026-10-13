using System.Reflection;
#if NET10_0_OR_GREATER
using DuckDbEfCoreBenchmarks;
#endif

namespace CrudBenchmarks.Tests;

public sealed class DuckDbEfProviderBenchmarkContractTests
{
#if NET10_0_OR_GREATER
    [Fact]
    public void DuckDbEfProviderBenchmark_ExposesLinqAndCompiledReadPaths()
    {
        var methods = typeof(DuckDbEfCoreEqualFootingBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ReadSingle_EntityFramework_StormGate", methods);
        Assert.Contains("ReadSingle_EntityFramework_Compiled_StormGate", methods);
        Assert.Contains("ReadSingle_EntityFramework_Compiled_NoStormGate", methods);
        Assert.Contains("Create_EntityFramework_SaveChanges_StormGate", methods);
    }
#endif
}
