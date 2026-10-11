using System;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// POOL-003 phase 1. One server has one connection budget no matter how many contexts point at it: contexts
/// register the usable limit they probed and the sizes of their governors, and the budget keeps the smallest usable
/// limit. It only engages (starts costing the hot path anything) once the registered sizes together exceed it.
/// Keys are unique per test because the registry is process-wide.
/// </summary>
public sealed class ServerBudgetRegistryTests
{
    private static string Key() => "test-" + Guid.NewGuid().ToString("N") + ":5432";

    [Fact]
    public void TwoRegistrationsForOneEndpoint_ShareOneBudget()
    {
        var key = Key();

        using var first = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        using var second = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);

        Assert.Same(first.Budget, second.Budget);
        Assert.Equal(2, first.Budget.Registrants);
    }

    [Fact]
    public void RegistrationsForDifferentEndpoints_DoNotShareABudget()
    {
        using var primary = ServerBudgetRegistry.Register(Key(), usableLimit: 22, configuredMaximum: 20);
        using var replica = ServerBudgetRegistry.Register(Key(), usableLimit: 60, configuredMaximum: 20);

        Assert.NotSame(primary.Budget, replica.Budget);
        Assert.Equal(22, primary.Budget.Limit);
        Assert.Equal(60, replica.Budget.Limit);
    }

    [Theory]
    [InlineData(22, 18, 18)]
    [InlineData(18, 22, 18)]
    [InlineData(22, 22, 22)]
    public void TheSmallestUsableLimitAmongRegistrantsWins(int firstLimit, int secondLimit, int expected)
    {
        var key = Key();

        using var first = ServerBudgetRegistry.Register(key, firstLimit, configuredMaximum: 10);
        using var second = ServerBudgetRegistry.Register(key, secondLimit, configuredMaximum: 10);

        Assert.Equal(expected, first.Budget.Limit);
    }

    [Fact]
    public void WhenTheRegisteredSizesFitTheLimit_TheBudgetStaysDisengaged()
    {
        // SQL Server's unlimited default is 32,767 against two pools of 100: it can never bind, so it never costs.
        using var reader = ServerBudgetRegistry.Register(Key(), usableLimit: 32767, configuredMaximum: 100);

        Assert.False(reader.Budget.Engaged);
    }

    [Fact]
    public void WhenTheRegisteredSizesTogetherExceedTheLimit_TheBudgetEngages()
    {
        var key = Key();

        using var reader = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        Assert.False(reader.Budget.Engaged); // 20 fits 22 on its own

        using var writer = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);

        Assert.True(writer.Budget.Engaged); // 20 + 20 > 22
    }

    [Fact]
    public void OnceEngaged_TheBudgetStaysEngaged_WhileAnyRegistrantRemains()
    {
        var key = Key();
        using var reader = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        var writer = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        Assert.True(reader.Budget.Engaged);

        writer.Dispose();

        // Latched: a context that registered earlier must not be left on the unguarded path by a later departure.
        Assert.True(reader.Budget.Engaged);
        Assert.Equal(1, reader.Budget.Registrants);
    }

    [Fact]
    public void TheLastRegistrationToLeave_RemovesTheBudget()
    {
        var key = Key();
        var first = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        var second = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        Assert.True(ServerBudgetRegistry.TryGet(key, out _));

        first.Dispose();
        Assert.True(ServerBudgetRegistry.TryGet(key, out _));

        second.Dispose();
        Assert.False(ServerBudgetRegistry.TryGet(key, out _));
    }

    [Fact]
    public void DisposingARegistrationTwice_LeavesTheOtherRegistrantsCountIntact()
    {
        var key = Key();
        var first = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        using var second = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, second.Budget.Registrants);
    }

    [Fact]
    public void ALaterRegistrantWithASmallerLimit_LowersTheSharedLimit_ForEveryone()
    {
        var key = Key();
        using var first = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 10);

        using var second = ServerBudgetRegistry.Register(key, usableLimit: 15, configuredMaximum: 10);

        Assert.Equal(15, first.Budget.Limit);
    }
}
