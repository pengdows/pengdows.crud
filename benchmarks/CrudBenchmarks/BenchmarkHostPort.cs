using System.Net;
using System.Net.Sockets;

namespace CrudBenchmarks;

/// <summary>
/// Picks the host port a benchmark container is published on. A benchmark that opens thousands of client
/// connections leaves them in TIME_WAIT inside the operating system's ephemeral range, and Docker allocates a
/// container's host port from that same range, so the next container start fails with "address already in use".
/// Ports are taken from below the range instead, where connection storms never land.
/// </summary>
internal static class BenchmarkHostPort
{
    // Linux's default ip_local_port_range starts at 32768; Windows and macOS start higher.
    internal const int EphemeralRangeStart = 32768;

    private const int Lowest = 20000;
    private static readonly object Gate = new();
    private static readonly HashSet<int> Handed = new();
    private static int _next = FirstCandidate(Random.Shared);

    // Random, not Lowest: each benchmark case is a fresh process, and Docker is still releasing the previous case's
    // container port when the next one starts.
    internal static int FirstCandidate(Random random) => random.Next(Lowest, EphemeralRangeStart);

    /// <summary>Returns a free TCP port in [20000, 32768) that this process has not handed out before.</summary>
    internal static int ReserveBelowEphemeralRange()
    {
        lock (Gate)
        {
            for (var attempts = 0; attempts < EphemeralRangeStart - Lowest; attempts++)
            {
                var port = _next;
                _next = _next + 1 >= EphemeralRangeStart ? Lowest : _next + 1;
                if (Handed.Contains(port) || !IsFree(port))
                {
                    continue;
                }

                Handed.Add(port);
                return port;
            }
        }

        throw new InvalidOperationException("No free TCP port below the ephemeral range.");
    }

    private static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
