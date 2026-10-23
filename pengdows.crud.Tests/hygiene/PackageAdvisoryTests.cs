using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PKCS-001: System.Security.Cryptography.Pkcs has advisories (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) and arrives
/// transitively through the database drivers. Only the driver-version matrix projects, which exist to run old drivers, may
/// suppress the audit; every other project that would pull it in pins a patched version (9.0.21 or later).
/// </summary>
public sealed class PackageAdvisoryTests
{
    private static readonly Version Patched = new(9, 0, 21);

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pengdows.crud.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static IEnumerable<string> Projects() =>
        Directory.EnumerateFiles(Root(), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static bool IsDriverMatrix(string project) =>
        Path.GetFileName(project).StartsWith("testbed.DriverVersionMatrix", StringComparison.Ordinal);

    [Fact]
    public void TheAuditExceptionIsConfinedToTheDriverVersionMatrixProjects()
    {
        var offenders = Projects()
            .Where(p => !IsDriverMatrix(p))
            .Where(p => File.ReadAllText(p).Contains("NuGetAuditSuppress", StringComparison.Ordinal) ||
                        Regex.IsMatch(File.ReadAllText(p), @"<NuGetAudit(?:Mode|Level)?>\s*(?:false|none)", RegexOptions.IgnoreCase))
            .Select(p => Path.GetRelativePath(Root(), p))
            .ToList();

        Assert.True(offenders.Count == 0, "Audit suppressed outside the driver matrix: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("pengdows.crud.Tests/pengdows.crud.Tests.csproj")]
    [InlineData("pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj")]
    [InlineData("testbed/testbed.csproj")]
    [InlineData("pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests/pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests.csproj")]
    public void ProjectsThatPullInPkcsPinAPatchedVersion(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Root(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var match = Regex.Match(text,
            @"<PackageReference\s+Include=""System\.Security\.Cryptography\.Pkcs""\s+Version=""(\d+\.\d+\.\d+)""");
        Assert.True(match.Success, relativePath + " does not pin System.Security.Cryptography.Pkcs");
        Assert.True(Version.Parse(match.Groups[1].Value) >= Patched,
            $"{relativePath} pins Pkcs {match.Groups[1].Value}, older than {Patched}");
    }

    // System.Drawing.Common 4.7.0 (GHSA-rxg9-xrhp-64gj, critical) arrives through System.Security.Permissions from the
    // Sybase ASE and Informix drivers. Nothing here draws, but the advisory is cleared by pinning a patched version
    // rather than suppressing it.
    [Theory]
    [InlineData("pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj")]
    [InlineData("testbed/testbed.csproj")]
    public void ProjectsThatPullInSystemDrawingCommonPinAPatchedVersion(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Root(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var match = Regex.Match(text,
            @"<PackageReference\s+Include=""System\.Drawing\.Common""\s+Version=""(\d+\.\d+\.\d+)""");
        Assert.True(match.Success, relativePath + " does not pin System.Drawing.Common");
        Assert.True(Version.Parse(match.Groups[1].Value) >= new Version(5, 0, 3),
            $"{relativePath} pins System.Drawing.Common {match.Groups[1].Value}, older than the patched 5.0.3");
    }

    [Fact]
    public void TheDriverMatrixProjectsThatSuppressTheAuditSuppressBothAdvisories()
    {
        var suppressing = Projects().Where(IsDriverMatrix)
            .Where(p => File.ReadAllText(p).Contains("NuGetAuditSuppress", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(suppressing);
        Assert.All(suppressing, p =>
        {
            var text = File.ReadAllText(p);
            Assert.Contains("GHSA-846f-f764-76mx", text, StringComparison.Ordinal);
            Assert.Contains("GHSA-2fmv-rv2c-59x7", text, StringComparison.Ordinal);
        });
    }
}
