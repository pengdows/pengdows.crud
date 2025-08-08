#region

using System;
using System.Threading;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EphemeralSecureStringTests
{
    [Fact]
    public void Reveal_ShouldReturnOriginalString()
    {
        var original = "supersecret";
        using var ess = new EphemeralSecureString(original);

        var revealed = ess.Reveal();

        Assert.Equal(original, revealed);
    }

    [Fact]
    public void WithRevealed_ShouldInvokeActionWithDecryptedValue()
    {
        var original = "topsecret";
        using var ess = new EphemeralSecureString(original);

        string? revealed = null;
        ess.WithRevealed(s => revealed = s);

        Assert.Equal(original, revealed);
    }

    [Fact]
    public void Reveal_ShouldReturnSameStringReference_IfCached()
    {
        var original = "cachetest";
        using var ess = new EphemeralSecureString(original);

        var first = ess.Reveal();
        var second = ess.Reveal();

        Assert.True(string.Compare(first, second, StringComparison.InvariantCulture) == 0);
    }

    [Fact]
    public void Reveal_ShouldClearCache_AfterTimeout()
    {
        var original = "timedout";
        using var ess = new EphemeralSecureString(original);

        var first = ess.Reveal();

        Thread.Sleep(1000); // wait > TTL (750ms)
        var second = ess.Reveal();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Reveal_ShouldThrow_AfterDisposal()
    {
        var original = "disposeme";
        var ess = new EphemeralSecureString(original);

        var decrypted = ess.Reveal();
        Assert.Equal(original, decrypted);

        ess.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ess.Reveal());
    }

    [Fact]
    public void Reveal_ShouldSupportUtf8Characters()
    {
        var original = "ümlaut-é-漢字"; // UTF-8 content
        using var ess = new EphemeralSecureString(original);

        var revealed = ess.Reveal();

        Assert.Equal(original, revealed);
    }
}