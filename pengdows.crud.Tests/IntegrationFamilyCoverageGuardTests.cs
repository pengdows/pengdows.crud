using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

// Reads the integration test SOURCE (no database, no container). A test class that lists some members of a database
// family by hand silently stops covering a member added later; that is how Aurora, SingleStore and the other newer
// products ended up running fewer tests than the product they delegate to. A family is listed once, in
// IntegrationTests/Infrastructure/DatabaseFamilies.cs; a test that needs fewer members takes the family and removes the
// ones it cannot run with a stated capability reason (DatabaseFamilies.PostgreSql.Except(...)), so the exclusion is
// visible and the new member is included by default.
public sealed class IntegrationFamilyCoverageGuardTests
{
    private static readonly string[] PostgreSqlFamily =
        { "PostgreSql", "AuroraPostgreSql", "CockroachDb", "YugabyteDb" };

    private static readonly string[] MySqlFamily =
        { "MySql", "AuroraMySql", "MariaDb", "TiDb", "SingleStore" };

    // Files whose literal lists are test data about the test infrastructure itself, not coverage choices.
    private static readonly string[] Allowed =
    {
        "Infrastructure/DatabaseFamilies.cs",
        "Infrastructure/DatabaseTestBaseInitializationOutcomeTests.cs",
        // The default enabled-provider list: every always-on database, not a coverage subset.
        "Infrastructure/IntegrationTestFixture.cs"
    };

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "pengdows.crud.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string[] IntegrationSources(string root) =>
        Directory.GetFiles(Path.Combine(root, "pengdows.crud.IntegrationTests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

    [Fact]
    public void NoIntegrationTest_ListsTwoMembersOfOneDatabaseFamilyByHand()
    {
        var root = Root();
        var violations = new System.Collections.Generic.List<string>();
        foreach (var file in IntegrationSources(root))
        {
            var relative = Path.GetRelativePath(Path.Combine(root, "pengdows.crud.IntegrationTests"), file)
                .Replace('\\', '/');
            if (Allowed.Contains(relative))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match run in Regex.Matches(text, @"SupportedDatabase\.\w+(?:\s*,\s*SupportedDatabase\.\w+)+"))
            {
                // A list handed to Except(...) names what a test leaves OUT of a family, which is the point.
                var before = text[Math.Max(0, run.Index - 24)..run.Index];
                if (before.Contains("Except("))
                {
                    continue;
                }

                var names = Regex.Matches(run.Value, @"SupportedDatabase\.(\w+)").Select(m => m.Groups[1].Value).ToList();
                foreach (var (label, family) in new[] { ("PostgreSQL", PostgreSqlFamily), ("MySQL", MySqlFamily) })
                {
                    if (names.Count(family.Contains) >= 2)
                    {
                        violations.Add($"{relative}: lists {string.Join(", ", names.Where(family.Contains))} " +
                                       $"by hand; use DatabaseFamilies.{(label == "PostgreSQL" ? "PostgreSql" : "MySql")}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DatabaseFamilies_NamesEveryMemberOfEachFamily()
    {
        var path = Path.Combine(Root(), "pengdows.crud.IntegrationTests", "Infrastructure", "DatabaseFamilies.cs");
        Assert.True(File.Exists(path), "pengdows.crud.IntegrationTests/Infrastructure/DatabaseFamilies.cs is missing");
        var text = File.ReadAllText(path);
        foreach (var name in PostgreSqlFamily.Concat(MySqlFamily))
        {
            Assert.Contains($"SupportedDatabase.{name}", text);
        }
    }
}
