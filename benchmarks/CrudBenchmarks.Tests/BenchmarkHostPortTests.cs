using System.Net;
using System.Net.Sockets;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// A benchmark that opens thousands of client connections leaves them in TIME_WAIT inside the operating system's
// ephemeral port range. Docker allocates a container's host port from the same range, so the next container
// start fails with "address already in use". The container's host port is therefore taken from below that range.
public class BenchmarkHostPortTests
{
    [Fact]
    public void ReservedPort_IsBelowTheEphemeralRange_AndFree()
    {
        var port = BenchmarkHostPort.ReserveBelowEphemeralRange();

        Assert.InRange(port, 1024, BenchmarkHostPort.EphemeralRangeStart - 1);

        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Start(); // throws when the port is not free
    }

    [Fact]
    public void ReservedPort_IsNotHandedOutAgainWhileTheProcessLives()
    {
        var ports = Enumerable.Range(0, 50).Select(_ => BenchmarkHostPort.ReserveBelowEphemeralRange()).ToList();

        Assert.Equal(ports.Count, ports.Distinct().Count());
    }

    [Fact]
    public void EphemeralRangeStart_MatchesTheOperatingSystemWhereItCanBeRead()
    {
        const string path = "/proc/sys/net/ipv4/ip_local_port_range";
        if (!File.Exists(path))
        {
            return; // not Linux: nothing to compare against
        }

        var start = int.Parse(File.ReadAllText(path).Split('\t', ' ')[0]);

        Assert.True(BenchmarkHostPort.EphemeralRangeStart <= start,
            $"The OS ephemeral range starts at {start}; ports must be reserved below {BenchmarkHostPort.EphemeralRangeStart}.");
    }

    // Every benchmark case runs in a fresh process, and Docker releases the previous case's container port a moment
    // after that process exits. A process that always started at the bottom of the range would pick the port its
    // predecessor was still releasing ("port is already allocated"), so the first candidate is random.
    [Fact]
    public void TheFirstCandidatePort_IsNotTheSameForEveryProcess()
    {
        var first = Enumerable.Range(0, 20)
            .Select(seed => BenchmarkHostPort.FirstCandidate(new Random(seed)))
            .ToList();

        Assert.All(first, port => Assert.InRange(port, 20000, BenchmarkHostPort.EphemeralRangeStart - 1));
        Assert.True(first.Distinct().Count() > 10, "The first candidate port barely varies between processes.");
    }
}
