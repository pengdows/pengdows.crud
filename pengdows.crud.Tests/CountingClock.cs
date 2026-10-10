using System;
using System.Diagnostics;
using System.Threading;

namespace pengdows.crud.Tests;

/// <summary>The real clock, counting how often it is read.</summary>
internal sealed class CountingClock : TimeProvider
{
    private int _reads;

    internal int Reads => Volatile.Read(ref _reads);

    public override long GetTimestamp()
    {
        Interlocked.Increment(ref _reads);
        return Stopwatch.GetTimestamp();
    }
}
