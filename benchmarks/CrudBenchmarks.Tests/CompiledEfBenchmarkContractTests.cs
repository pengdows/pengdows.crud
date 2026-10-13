using System.Reflection;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

public sealed class CompiledEfBenchmarkContractTests
{
    [Fact]
    public void EqualFootingBenchmark_ExposesUncompiledAndCompiledLinqReadPaths()
    {
        var methods = typeof(EqualFootingCrudBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ReadSingle_EntityFramework_Linq", methods);
        Assert.Contains("ReadSingle_EntityFramework_Compiled", methods);
    }
}
