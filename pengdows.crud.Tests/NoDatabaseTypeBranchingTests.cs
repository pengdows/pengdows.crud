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
/// <c>product == X</c>, no <c>switch (product)</c>, no table keyed by product. The only switch on the
/// database is the one that creates the dialects (<c>SqlDialectFactory</c>, in <c>dialects/</c>), fed by
/// <c>DatabaseDetectionService</c>. <c>SupportedDatabase.Unknown</c> as a "no database" value is allowed.
/// </summary>
public sealed class NoDatabaseTypeBranchingTests
{
    private static readonly Regex NamedDatabase = new(@"SupportedDatabase\.(?!Unknown\b)[A-Z][A-Za-z0-9]*",
        RegexOptions.Compiled);

    // Files still being moved into the dialects, with their current count. The count may only go
    // down; a file reaching zero must be removed from this list.
    private static readonly Dictionary<string, int> NotYetMoved = new(StringComparer.Ordinal)
    {
        ["types/AdvancedTypeRegistry.cs"] = 78,
        ["isolation/IsolationResolver.cs"] = 45,
        ["exceptions/translators/DbExceptionTranslatorRegistry.cs"] = 23,
        ["types/converters/SpatialConverter.cs"] = 14,
        ["types/coercion/AdvancedCoercions.cs"] = 8,
        ["types/converters/InetConverter.cs"] = 3,
        ["types/converters/PostgreSqlIntervalConverter.cs"] = 3,
        ["types/converters/IntervalDaySecondConverter.cs"] = 3,
        ["types/converters/IntervalYearMonthConverter.cs"] = 3,
        ["types/converters/PostgreSqlRangeConverter.cs"] = 3,
        ["types/converters/CidrConverter.cs"] = 3,
    };

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
            .Where(x => !NotYetMoved.TryGetValue(x.File, out var allowed) || x.Count > allowed)
            .Select(x => $"{x.File}: {x.Count} reference(s) to a specific SupportedDatabase")
            .Concat(NotYetMoved
                .Where(kv => counts.All(x => x.File != kv.Key || x.Count < kv.Value))
                .Select(kv => $"{kv.Key}: now {counts.FirstOrDefault(x => x.File == kv.Key).Count}, lower its entry (was {kv.Value})"))
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
