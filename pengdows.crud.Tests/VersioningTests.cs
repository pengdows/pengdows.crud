using System.Reflection;
using System.Text.RegularExpressions;
using pengdows.crud;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class VersioningTests
{
    private static readonly Regex SemVerRegex = new(
        "^1\\.1\\.\\d+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void InformationalVersion_UsesStandardSemVer_ForCoreAssemblies()
    {
        AssertSemVer(typeof(DatabaseContext).Assembly);
#pragma warning disable CS0618
        AssertSemVer(typeof(IEntityHelper<,>).Assembly);
#pragma warning restore CS0618
        AssertSemVer(typeof(fakeDbConnection).Assembly);
    }

    private static void AssertSemVer(Assembly assembly)
    {
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(infoVersion));
        Assert.Matches(SemVerRegex, infoVersion!);
    }
}