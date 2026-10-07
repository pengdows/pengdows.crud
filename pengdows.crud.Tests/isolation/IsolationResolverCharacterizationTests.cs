using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.isolation;
using Xunit;

namespace pengdows.crud.Tests.isolation;

/// <summary>
/// DEC-010: pins every output of the isolation mapping (supported levels, each profile's
/// resolution and degraded flag, ResolveAtLeast and ResolveForTransaction for every level and
/// profile) for every SupportedDatabase value, snapshot setting and RCSI setting, so moving the
/// mapping out of IsolationResolver's switch and into the dialects changes nothing.
/// PIN_ISOLATION=1 rewrites the pinned file from the current code.
/// </summary>
public class IsolationResolverCharacterizationTests
{
    private static readonly IsolationLevel[] Levels =
    {
        IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted, IsolationLevel.RepeatableRead,
        IsolationLevel.Snapshot, IsolationLevel.Serializable
    };

    private static string PinnedPath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "IsolationResolverCharacterization.txt");

    internal static string Describe(Func<SupportedDatabase, bool, bool, IsolationResolver> create)
    {
        var text = new StringBuilder();
        foreach (var product in Enum.GetValues<SupportedDatabase>())
        {
            foreach (var snapshot in new[] { false, true })
            {
                foreach (var rcsi in new[] { false, true })
                {
                    var resolver = create(product, rcsi, snapshot);
                    text.Append(product).Append(" snapshot=").Append(snapshot).Append(" rcsi=").Append(rcsi).AppendLine();
                    text.Append("  levels: ")
                        .AppendLine(string.Join(",", resolver.GetSupportedLevels().OrderBy(l => (int)l)));
                    foreach (var profile in Enum.GetValues<IsolationProfile>())
                    {
                        text.Append("  ").Append(profile).Append(": ")
                            .Append(Outcome(() =>
                            {
                                var r = resolver.ResolveWithDetail(profile);
                                return $"{r.Level} degraded={r.Degraded}";
                            }))
                            .Append(" | tx ").AppendLine(Outcome(() => resolver.ResolveForTransaction(profile).ToString()));
                    }

                    foreach (var level in Levels)
                    {
                        text.Append("  atLeast ").Append(level).Append(": ")
                            .AppendLine(Outcome(() => resolver.ResolveAtLeast(level).ToString()));
                    }
                }
            }
        }

        return text.ToString();
    }

    private static string Outcome(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Exception ex)
        {
            return "throws " + ex.GetType().Name;
        }
    }

    internal static IsolationResolver Create(SupportedDatabase product, bool rcsi, bool snapshot) =>
        new(IsolationTestDialectFactory.Create(product), rcsi, snapshot);

    [Fact]
    public void EveryDatabase_ResolvesExactlyAsPinned()
    {
        var actual = Describe(Create);
        if (Environment.GetEnvironmentVariable("PIN_ISOLATION") == "1")
        {
            File.WriteAllText(PinnedPath(), actual);
        }

        // Compared with line endings normalized: git may check the pinned file out as CRLF on Windows.
        Assert.Equal(File.ReadAllText(PinnedPath()).ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));
    }
}
