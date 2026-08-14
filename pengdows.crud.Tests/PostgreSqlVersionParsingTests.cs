using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PostgreSQL's "SELECT version()" banner embeds the compiler's own dotted version number at the
/// end of the string (e.g. "..., compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit"). Discovered
/// against a REAL PostgreSQL 18.1 Testcontainers instance while validating
/// <see cref="pengdows.crud.dialects.ISqlDialect.EmitsAnsiMergeSyntax"/> integration coverage —
/// fakeDb-only unit tests never execute a real "SELECT version()" round-trip and so never
/// surfaced this. The base <see cref="SqlDialect.ParseVersion"/> picks the LAST dotted-number
/// match in the string, which is the gcc version, not the actual server version. On real
/// gcc-built PostgreSQL servers (the default on Linux, i.e. virtually every Docker image) this
/// silently disabled every IsVersionAtLeast()-gated capability (SupportsMerge, SupportsJsonTypes,
/// SupportsSqlJsonConstructors, SupportsJsonTable, SupportsMergeReturning) regardless of the
/// server's real version.
/// </summary>
public class PostgreSqlVersionParsingTests
{
    [Theory]
    // Real banner captured from a "postgres:latest" (18.1) Testcontainers instance. The base
    // SqlDialect.ParseVersion regex previously picked up "14.2.0" (the gcc version) instead of
    // "18.1" (the actual server version) because it takes the LAST dotted-number match.
    [InlineData(
        "PostgreSQL 18.1 (Debian 18.1-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit",
        "18.1")]
    [InlineData(
        "PostgreSQL 15.4 on x86_64-pc-linux-gnu, compiled by gcc (GCC) 8.5.0 20210514 (Red Hat 8.5.0-18), 64-bit",
        "15.4")]
    [InlineData("PostgreSQL 12.0", "12.0")]
    [InlineData("not a postgres banner", null)]
    public void ParseVersion_ExtractsServerVersion_NotCompilerVersion(string banner, string? expected)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);

        var parsed = dialect.ParseVersion(banner);

        if (expected == null)
        {
            Assert.Null(parsed);
        }
        else
        {
            Assert.NotNull(parsed);
            Assert.Equal(expected, $"{parsed!.Major}.{parsed.Minor}");
        }
    }

    [Fact]
    public void ParseVersion_RealWorldGccBanner_Major18_NotGccMajor14()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);

        var parsed = dialect.ParseVersion(
            "PostgreSQL 18.1 (Debian 18.1-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit");

        Assert.NotNull(parsed);
        Assert.Equal(18, parsed!.Major);
    }
}
