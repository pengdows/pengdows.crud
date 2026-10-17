using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Provenance: docs/facts.json is the machine-readable form of docs/integration-status.md, written by the same script
/// (tools/record-integration-run.py) from the same cells, so a number quoted anywhere can be traced to a run and a commit.
/// </summary>
public sealed class FactsJsonTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pengdows.crud.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static readonly Regex Cell = new(
        @"(\d+) pass, (\d+) fail, (\d+) skip · (\d{4}-\d\d-\d\d \d\d:\d\d)Z · ([0-9a-f]{8})(\+?)", RegexOptions.Compiled);

    [Fact]
    public void FactsJson_SaysExactlyWhatTheIntegrationStatusDocSays()
    {
        var doc = File.ReadAllLines(Path.Combine(Root(), "docs", "integration-status.md"));
        using var facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "docs", "facts.json")));
        var integration = facts.RootElement.GetProperty("integration");
        var frameworks = new[] { "net8.0", "net10.0" };

        var compared = 0;
        foreach (var line in doc.Where(l => l.StartsWith("| ", StringComparison.Ordinal)))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            for (var i = 0; i < frameworks.Length; i++)
            {
                var match = Cell.Match(cells.Length > i + 1 ? cells[i + 1] : "");
                if (!match.Success)
                {
                    continue;
                }

                var fact = integration.GetProperty(cells[0]).GetProperty(frameworks[i]);
                Assert.Equal(int.Parse(match.Groups[1].Value), fact.GetProperty("pass").GetInt32());
                Assert.Equal(int.Parse(match.Groups[2].Value), fact.GetProperty("fail").GetInt32());
                Assert.Equal(int.Parse(match.Groups[3].Value), fact.GetProperty("skip").GetInt32());
                Assert.Equal(match.Groups[4].Value + "Z", fact.GetProperty("runUtc").GetString());
                Assert.Equal(match.Groups[5].Value, fact.GetProperty("commit").GetString());
                Assert.Equal(match.Groups[6].Value == "+", fact.GetProperty("dirtyTree").GetBoolean());
                compared++;
            }
        }

        Assert.True(compared >= 34, $"only {compared} cells compared");
        Assert.Equal("tools/record-integration-run.py", facts.RootElement.GetProperty("generatedBy").GetString());
    }
}
