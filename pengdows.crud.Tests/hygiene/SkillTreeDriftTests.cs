using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// skills/claude, skills/codex and skills/gemini carry the same pengdows-crud skill content.
/// Layout differs (claude/ is flat; codex/ and gemini/ nest reference files under references/),
/// content must not. tools/check-skill-drift.sh runs the same check in CI.
/// </summary>
public class SkillTreeDriftTests
{
    private const string Claude = "skills/claude/pengdows-crud";

    public static IEnumerable<object[]> OtherTrees() => new[]
    {
        new object[] { "skills/codex/pengdows-crud" },
        new object[] { "skills/gemini/pengdows-crud" }
    };

    [Theory]
    [MemberData(nameof(OtherTrees))]
    public void SkillTree_MatchesTheClaudeTree(string otherTree)
    {
        var root = GetRepoRoot();
        var claude = Path.Combine(root, Claude);
        var other = Path.Combine(root, otherTree);
        var drift = new List<string>();

        foreach (var file in Directory.GetFiles(claude, "*.md"))
        {
            var name = Path.GetFileName(file);
            var counterpart = name == "SKILL.md"
                ? Path.Combine(other, name)
                : Path.Combine(other, "references", name);
            if (!File.Exists(counterpart))
            {
                drift.Add($"{name}: missing in {otherTree}");
            }
            else if (File.ReadAllText(file) != File.ReadAllText(counterpart))
            {
                drift.Add($"{name}: differs in {otherTree}");
            }
        }

        foreach (var file in Directory.GetFiles(Path.Combine(other, "references"), "*.md"))
        {
            var name = Path.GetFileName(file);
            if (name == "SKILL.md")
            {
                // A second SKILL.md under references/ is never compared, so it goes stale unseen.
                drift.Add($"references/SKILL.md: stray copy of SKILL.md in {otherTree}");
            }
            else if (!File.Exists(Path.Combine(claude, name)))
            {
                drift.Add($"{name}: only in {otherTree}");
            }
        }

        Assert.True(drift.Count == 0, string.Join(Environment.NewLine, drift));
    }

    [Fact]
    public void DriftScript_IsWiredIntoCi()
    {
        var root = GetRepoRoot();

        Assert.True(File.Exists(Path.Combine(root, "tools", "check-skill-drift.sh")));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "skill-drift.yml"));
        Assert.Contains("tools/check-skill-drift.sh", workflow, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "pengdows.crud.sln")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for skill drift validation.");
    }
}
