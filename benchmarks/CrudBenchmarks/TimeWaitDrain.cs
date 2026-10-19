namespace CrudBenchmarks;

/// <summary>
/// Waits for the host's TIME_WAIT sockets to drain before a storm case starts its container. An uncontrolled storm
/// leaves tens of thousands of them (34,309 after one arm on 2026-10-11), filling the ephemeral port range that Docker's
/// proxy and the next container need, so the next case failed in setup with "Connection reset by peer". Linux only:
/// elsewhere there is no /proc/net/tcp and the wait is skipped.
/// </summary>
internal static class TimeWaitDrain
{
    internal const int DefaultThreshold = 2000;
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private const string TimeWaitState = "06";
    private static readonly string[] Tables = { "/proc/net/tcp", "/proc/net/tcp6" };

    internal static int CountTimeWait(IEnumerable<string> procNetTcpLines)
    {
        var count = 0;
        foreach (var line in procNetTcpLines)
        {
            // "sl local_address rem_address st ...": the state is the fourth field; the header's is "st".
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 3 && fields[0].EndsWith(':') && fields[3] == TimeWaitState)
            {
                count++;
            }
        }

        return count;
    }

    internal static int CountHostTimeWait() =>
        Tables.Where(File.Exists).Sum(path => CountTimeWait(File.ReadLines(path)));

    /// <summary>Returns true once the count is below <paramref name="threshold"/>, false if it never got there.</summary>
    internal static async Task<bool> WaitAsync(Func<int> count, int threshold, TimeSpan timeout, TimeSpan poll)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (count() < threshold)
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(poll);
        }
    }

    /// <summary>Waits on this host's sockets and logs what it found; never fails the case, only delays it.</summary>
    internal static async Task WaitForHostAsync(string tag)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var before = CountHostTimeWait();
        var drained = await WaitAsync(CountHostTimeWait, DefaultThreshold, DefaultTimeout, TimeSpan.FromSeconds(2));
        Console.WriteLine($"[{tag}] TIME_WAIT before setup: {before}, after waiting: {CountHostTimeWait()}" +
                          (drained ? string.Empty : $" (still above {DefaultThreshold} after {DefaultTimeout.TotalSeconds:F0} s)"));
    }
}
