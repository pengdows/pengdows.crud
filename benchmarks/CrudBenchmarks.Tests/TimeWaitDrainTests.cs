using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// A connection storm leaves tens of thousands of sockets in TIME_WAIT, which use up the ephemeral port range the next
// case's container start needs (2026-10-11: 34,309 after one uncontrolled arm). Storm cases wait for the count to drain
// first; the count is read from /proc/net/tcp and tcp6, where TIME_WAIT is state 06.
public class TimeWaitDrainTests
{
    private const string Header =
        "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode";

    [Fact]
    public void CountTimeWait_CountsOnlyStateSix_AndSkipsTheHeader()
    {
        var lines = new[]
        {
            Header,
            "   0: 0100007F:1538 0100007F:A1B2 06 00000000:00000000 03:00000123 00000000     0        0 0",
            "   1: 0100007F:1538 0100007F:A1B3 01 00000000:00000000 00:00000000 00000000  1000        0 12345",
            "   2: 0100007F:1538 0100007F:A1B4 06 00000000:00000000 03:00000456 00000000     0        0 0",
            "   3: 00000000:0016 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 999"
        };

        Assert.Equal(2, TimeWaitDrain.CountTimeWait(lines));
    }

    [Fact]
    public void CountTimeWait_IgnoresBlankAndMalformedLines()
    {
        Assert.Equal(0, TimeWaitDrain.CountTimeWait(new[] { Header, "", "garbage" }));
    }

    [Fact]
    public async Task WaitAsync_ReturnsAtOnce_WhenTheCountIsAlreadyBelowTheThreshold()
    {
        var reads = 0;
        var drained = await TimeWaitDrain.WaitAsync(() => { reads++; return 10; }, threshold: 2000,
            timeout: TimeSpan.FromSeconds(5), poll: TimeSpan.FromMilliseconds(1));

        Assert.True(drained);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task WaitAsync_Polls_UntilTheCountFalls()
    {
        var counts = new Queue<int>(new[] { 30_000, 9_000, 1_500 });
        var drained = await TimeWaitDrain.WaitAsync(() => counts.Dequeue(), threshold: 2000,
            timeout: TimeSpan.FromSeconds(5), poll: TimeSpan.FromMilliseconds(1));

        Assert.True(drained);
        Assert.Empty(counts);
    }

    [Fact]
    public async Task WaitAsync_GivesUp_AfterTheTimeout()
    {
        var drained = await TimeWaitDrain.WaitAsync(() => 30_000, threshold: 2000,
            timeout: TimeSpan.FromMilliseconds(30), poll: TimeSpan.FromMilliseconds(5));

        Assert.False(drained);
    }
}
