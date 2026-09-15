using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CockroachDbDialect inherits from PostgreSqlDialect without overriding ParseVersion.
/// PostgreSqlDialect.ParseVersion only activates its gcc-collision fix when the banner contains
/// the literal token "PostgreSQL" — CockroachDB's real "SELECT version()" banner does not, so it
/// falls through to the base SqlDialect.ParseVersion, which takes the LAST dotted-number match in
/// the string. That grabs the Go toolchain version (e.g. "1.19.13") instead of the real product
/// version (e.g. "23.1.30"), silently breaking every IsVersionAtLeast()-gated capability check.
///
/// Banner captured live: `docker run cockroachdb/cockroach:latest-v23.1 start-single-node
/// --insecure`, then `cockroach sql --insecure -e "SELECT version();"` →
/// "CockroachDB CCL v23.1.30 (x86_64-pc-linux-gnu, built 2024/12/09 17:37:15, go1.19.13)".
/// </summary>
public class CockroachDbVersionParsingTests
{
    [Theory]
    [InlineData(
        "CockroachDB CCL v23.1.30 (x86_64-pc-linux-gnu, built 2024/12/09 17:37:15, go1.19.13)",
        "23.1")]
    [InlineData(
        "CockroachDB CCL v22.2.19 (x86_64-pc-linux-gnu, built 2023/12/11 18:12:22, go1.19.13)",
        "22.2")]
    [InlineData("not a cockroach banner", null)]
    public void ParseVersion_ExtractsServerVersion_NotGoToolchainVersion(string banner, string? expected)
    {
        var factory = new fakeDbFactory(SupportedDatabase.CockroachDb);
        var dialect = new CockroachDbDialect(factory, NullLogger<CockroachDbDialect>.Instance);

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
    public void ParseVersion_RealWorldBanner_Major23_NotGoMajor1()
    {
        var factory = new fakeDbFactory(SupportedDatabase.CockroachDb);
        var dialect = new CockroachDbDialect(factory, NullLogger<CockroachDbDialect>.Instance);

        var parsed = dialect.ParseVersion(
            "CockroachDB CCL v23.1.30 (x86_64-pc-linux-gnu, built 2024/12/09 17:37:15, go1.19.13)");

        Assert.NotNull(parsed);
        Assert.Equal(23, parsed!.Major);
    }
}
