using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A context opens connections from many threads at once, so the factory that hands them out must be safe to call that way
/// (found by the pool boundary stress test: staged connections were lost, handed out twice, or threw a NullReferenceException).
/// </summary>
public sealed class FakeDbFactoryConcurrencyTests
{
    [Fact]
    public void ConcurrentCreateConnection_HandsEachStagedConnectionOutExactlyOnce()
    {
        const int staged = 400;
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var stagedConnections = Enumerable.Range(0, staged).Select(_ => new fakeDbConnection()).ToList();
        foreach (var connection in stagedConnections)
        {
            factory.Connections.Add(connection);
        }

        var handedOut = new ConcurrentBag<object>();
        var failures = new ConcurrentBag<Exception>();
        using var start = new ManualResetEventSlim(false);
        var threads = Enumerable.Range(0, 16).Select(_ => new Thread(() =>
        {
            start.Wait();
            for (var i = 0; i < staged / 16; i++)
            {
                try
                {
                    handedOut.Add(factory.CreateConnection());
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        start.Set();
        threads.ForEach(t => t.Join());

        Assert.Empty(failures);
        Assert.Equal(staged, handedOut.Count);
        Assert.Equal(staged, handedOut.Distinct().Count());
        Assert.All(handedOut, c => Assert.Contains(c, stagedConnections));
        Assert.Equal(staged, factory.CreatedConnections.Count);
    }
}
