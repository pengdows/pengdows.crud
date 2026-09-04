using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies core assemblies carry a standard SemVer InformationalVersion matching this branch's
/// actual configured version.
///
/// The expected version prefix is read directly from the repo-root Directory.Build.props'
/// &lt;VersionPrefix&gt; -- the single source of truth all core assemblies derive their
/// AssemblyInformationalVersion from (see Directory.Build.props: Version/AssemblyVersion/
/// FileVersion all derive from VersionPrefix) -- rather than a second, independently
/// hardcoded literal in this test. A previous version of this test hardcoded "2.0.6" directly in
/// its regex; a later version-bump commit updated Directory.Build.props to 2.1.0 but not this
/// test, so it started failing for a stale-expectation reason having nothing to do with actual
/// versioning correctness. Deriving the expected prefix from Directory.Build.props itself makes
/// that class of drift structurally impossible going forward.
/// </summary>
public sealed class VersioningTests
{
    [Fact]
    public void InformationalVersion_UsesStandardSemVer_ForCoreAssemblies()
    {
        var versionPrefix = ReadVersionPrefixFromDirectoryBuildProps();
        var semVerRegex = new Regex(
            $"^{Regex.Escape(versionPrefix)}(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        AssertSemVer(typeof(DatabaseContext).Assembly, semVerRegex);
        AssertSemVer(typeof(TableGateway<,>).Assembly, semVerRegex);
        AssertSemVer(typeof(fakeDbConnection).Assembly, semVerRegex);
    }

    private static void AssertSemVer(Assembly assembly, Regex semVerRegex)
    {
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(infoVersion));
        Assert.Matches(semVerRegex, infoVersion!);
    }

    private static string ReadVersionPrefixFromDirectoryBuildProps()
    {
        var propsPath = Path.Combine(GetRepoRoot(), "Directory.Build.props");
        var contents = File.ReadAllText(propsPath);
        var match = Regex.Match(contents, @"<VersionPrefix>\s*([^<\s]+)\s*</VersionPrefix>");
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Could not find <VersionPrefix> in '{propsPath}' -- update this test's parsing " +
                "if Directory.Build.props' versioning scheme changed.");
        }

        return match.Groups[1].Value;
    }

    private static string GetRepoRoot()
    {
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = start; current != null; current = current.Parent)
        {
            var slnPath = Path.Combine(current.FullName, "pengdows.crud.sln");
            if (File.Exists(slnPath))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for version validation.");
    }
}
