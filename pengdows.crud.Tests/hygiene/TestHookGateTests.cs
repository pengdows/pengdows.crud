using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Test-only hooks in the library (<c>TestOnly*</c> members) exist only in builds that define
/// <c>PENGDOWS_CRUD_TEST_HOOKS</c>, which only the test project sets. A hook that ships in a release build is a
/// production surface that exists for tests, so every declaration and every use must sit inside that gate.
/// </summary>
public class TestHookGateTests
{
    private const string Gate = "#if PENGDOWS_CRUD_TEST_HOOKS";

    private static readonly Regex Use = new(@"\bTestOnly\w+", RegexOptions.Compiled);

    private static IEnumerable<string> LibrarySources()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var library = Path.Combine(root, "pengdows.crud");
        return Directory.EnumerateFiles(library, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    // The nearest line above that is not a comment or blank must be the gate.
    private static bool IsGated(IReadOnlyList<string> lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed == Gate;
        }

        return false;
    }

    [Fact]
    public void EveryTestOnlyHookIsGatedOutOfReleaseBuilds()
    {
        var violations = new List<string>();

        foreach (var file in LibrarySources())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Use.IsMatch(lines[i]) || lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsGated(lines, i))
                {
                    violations.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Ungated test-only hooks (must sit inside #if PENGDOWS_CRUD_TEST_HOOKS):\n" + string.Join("\n", violations));
    }
}
