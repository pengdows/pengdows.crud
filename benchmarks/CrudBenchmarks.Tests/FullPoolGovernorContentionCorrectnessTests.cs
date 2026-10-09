using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using BenchmarkDotNet.Attributes;
using System.Reflection;

namespace CrudBenchmarks.Tests;

public sealed class FullPoolGovernorContentionCorrectnessTests
{
    [Fact]
    public void FullContentionBenchmarkDoesNotAddASecondJobToTheInProcessHarness()
    {
        Assert.Null(typeof(FullPoolGovernorContentionBenchmarks)
            .GetCustomAttribute<SimpleJobAttribute>());
    }

    [Fact]
    public void AdaptiveFullCopy_UsesTheGateDirectly()
    {
        var source = File.ReadAllText(FindBenchmarkSource("PoolGovernorAdaptiveFullCopy.cs"));

        Assert.DoesNotContain("PoolGovernorAdmissionAdapter", source, StringComparison.Ordinal);
        Assert.Contains("PoolGovernorConcurrencyGate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FullContentionBenchmark_HasABoundedWatchdog()
    {
        var source = File.ReadAllText(FindBenchmarkSource("FullPoolGovernorContentionBenchmarks.cs"));

        Assert.Contains("ContentionWatchdogTimeout", source, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(ContentionWatchdogTimeout)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdaptiveFullCopy_CompletesUnderFourSlotContention()
    {
        using var governor = new PoolGovernorAdaptiveCopy(
            PoolLabel.Writer,
            "contention-test",
            maxSlots: 4,
            acquireTimeout: TimeSpan.FromSeconds(5));

        var workers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            for (var operation = 0; operation < 100; operation++)
            {
                await using var slot = await governor.AcquireAsync();
                await Task.Yield();
            }
        })).ToArray();

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DirectGateFullCopy_CompletesUnderFourSlotContention()
    {
        using var governor = new DirectPoolGovernorContentionCopy(
            capacity: 4,
            maxQueueDepth: 256);

        var workers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            for (var operation = 0; operation < 100; operation++)
            {
                await using var slot = await governor.AcquireAsync();
                await Task.Yield();
            }
        })).ToArray();

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static string FindBenchmarkSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "..", "..", "..", "..", "CrudBenchmarks", fileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(fileName);
    }
}
