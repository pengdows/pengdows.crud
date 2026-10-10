using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The concurrency gate is the pool governor's only admission primitive. A SemaphoreSlim admission path used to
/// sit beside it so the two could be benchmarked against each other; the benchmark settled it, and a second path
/// that production never takes is dead code that tests and readers still have to reason about.
/// </summary>
public class PoolGovernorGateOnlyAdmissionTests
{
    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void NoConstructor_AcceptsASemaphoreForAdmission()
    {
        // The turnstile is a SemaphoreSlim and stays; nothing else may be.
        var semaphoreParameters = typeof(PoolGovernor)
            .GetConstructors(AllInstance)
            .SelectMany(c => c.GetParameters())
            .Where(p => p.ParameterType == typeof(SemaphoreSlim))
            .Select(p => p.Name)
            .Distinct()
            .ToArray();

        Assert.Equal(new[] { "turnstile" }, semaphoreParameters);
    }

    [Fact]
    public void TheGovernor_HoldsNoAdmissionSemaphore()
    {
        var semaphoreMembers = typeof(PoolGovernor)
            .GetMembers(AllInstance)
            .Where(m => m.Name is "_semaphore" or "_ownsSemaphore" or "OwnsSemaphore")
            .Select(m => m.Name)
            .ToArray();

        Assert.Empty(semaphoreMembers);
    }

    [Fact]
    public void ADefaultGovernor_AdmitsThroughTheGate()
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "gate-only", 2, TimeSpan.FromSeconds(5));

        var gateField = typeof(PoolGovernor).GetField("_concurrencyGate", AllInstance);
        Assert.NotNull(gateField);
        Assert.NotNull(gateField!.GetValue(governor));
        Assert.Equal(2, governor.AvailablePermits);

        using (governor.Acquire())
        {
            Assert.Equal(1, governor.AvailablePermits);
        }

        Assert.Equal(2, governor.AvailablePermits);
    }
}
