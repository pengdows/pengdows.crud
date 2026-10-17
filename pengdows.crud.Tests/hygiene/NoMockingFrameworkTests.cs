using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Moq is never used in this repository, nor any other mocking framework or generated proxy. fakeDb is the test provider, and
/// anything it cannot do is added to fakeDb; a test that needs an implementer of a library interface uses a hand-written stub
/// (tools/test-stub-generator writes them). This fails on a package reference, a using, or a construction of one.
/// </summary>
public class NoMockingFrameworkTests
{
    private static readonly string[] ExcludedFolders =
        { "bin", "obj", ".git", "TestResults", "artifacts", "BenchmarkDotNet.Artifacts", "node_modules" };

    private static readonly string[] Extensions = { ".cs", ".csproj", ".props", ".targets" };

    // What a mock looks like in source. Words in comments ("Moq is never used") are not matched.
    private static readonly Regex Forbidden = new(
        @"using\s+Moq\b|\bMoq\.(?:Protected|Mock|Language)|PackageReference\s+Include=""(?:Moq|NSubstitute|FakeItEasy)""" +
        @"|\bnew\s+(?:Moq\.)?Mock<|\bMock\.Of<|\bDispatchProxy\b|\bSubstitute\.For<|\bA\.Fake<",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pengdows.crud.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("pengdows.crud.sln not found above the test output");
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => ExcludedFolders.Contains(part)))
            // This file names the patterns it forbids.
            .Where(f => !string.Equals(Path.GetFileName(f), nameof(NoMockingFrameworkTests) + ".cs", StringComparison.Ordinal));

    [Fact]
    public void NoMockingFrameworkIsReferencedOrUsedAnywhereInTheRepository()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Forbidden.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A mocking framework is in use (use fakeDb, or a hand-written stub; add what fakeDb lacks to fakeDb):\n" +
            string.Join("\n", offenders));
    }
}
