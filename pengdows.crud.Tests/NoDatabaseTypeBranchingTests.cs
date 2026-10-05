using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-039 (user rule, 2026-10-03): per-database behavior lives in the dialects. Outside
/// <c>dialects/</c> no code may name a specific <c>SupportedDatabase</c> value: no
/// <c>product == X</c>, no <c>switch (product)</c>, no table keyed by product. The only switches on the
/// database are in <c>SqlDialectFactory</c> (in <c>dialects/</c>): the one that creates the dialects, fed
/// by <c>DatabaseDetectionService</c>, and the one that maps each value to the <c>DatabaseTraits</c> its
/// dialect declares (exception translator, type mappings, coercions, value-converter formats). <c>SupportedDatabase.Unknown</c> as a "no database" value is allowed.
/// </summary>
public sealed class NoDatabaseTypeBranchingTests
{
    private static readonly Regex NamedDatabase = new(@"SupportedDatabase\.(?!Unknown\b)[A-Z][A-Za-z0-9]*",
        RegexOptions.Compiled);

    private static readonly string[] AllowedFiles =
    {
        "internal/DatabaseDetectionService.cs" // produces the value that picks the dialect
    };

    [Fact]
    public void NoCodeOutsideTheDialectsNamesASpecificDatabase()
    {
        var root = Path.Combine(RepoRoot(), "pengdows.crud");
        var counts = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("dialects/", StringComparison.Ordinal) &&
                        !f.StartsWith("bin/", StringComparison.Ordinal) &&
                        !f.StartsWith("obj/", StringComparison.Ordinal) &&
                        !AllowedFiles.Contains(f))
            .Select(f => (File: f, Count: CodeLines(Path.Combine(root, f)).Sum(l => NamedDatabase.Matches(l).Count)))
            .Where(x => x.Count > 0)
            .ToList();

        var problems = counts
            .Select(x => $"{x.File}: {x.Count} reference(s) to a specific SupportedDatabase")
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // Comments may name databases; only code counts.
    private static IEnumerable<string> CodeLines(string path) =>
        File.ReadLines(path)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("*", StringComparison.Ordinal))
            .Select(l => l.Contains("//", StringComparison.Ordinal) ? l[..l.IndexOf("//", StringComparison.Ordinal)] : l);

    private static string RepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "pengdows.crud.sln")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
