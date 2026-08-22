using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Unlike CockroachDbDialect (see CockroachDbVersionParsingTests), YugabyteDbDialect does NOT
/// need its own ParseVersion override. Its real "SELECT version()" banner contains the literal
/// token "PostgreSQL" followed directly by the real YSQL-compatibility version, so the inherited
/// PostgreSqlDialect.ParseVersion override's "PostgreSQL X.Y" regex matches and extracts the
/// correct version directly — it never falls through to the base SqlDialect.ParseVersion's
/// "last dotted number" fallback that mis-parses CockroachDB's banner.
///
/// Banner captured live: `docker run yugabytedb/yugabyte:latest bin/yugabyted start
/// --daemon=false`, then `bin/ysqlsh -h &lt;container-ip&gt; -U yugabyte -c "SELECT version();"` →
/// "PostgreSQL 15.12-YB-2.25.2.0-b0 on x86_64-pc-linux-gnu, compiled by clang version 19.1.0
/// (...), 64-bit".
/// </summary>
public class YugabyteDbVersionParsingTests
{
    [Fact]
    public void ParseVersion_RealWorldBanner_ExtractsYsqlCompatibilityVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.YugabyteDb);
        var dialect = new YugabyteDbDialect(factory, NullLogger<YugabyteDbDialect>.Instance);

        var parsed = dialect.ParseVersion(
            "PostgreSQL 15.12-YB-2.25.2.0-b0 on x86_64-pc-linux-gnu, compiled by clang version 19.1.0 " +
            "(https://github.com/yugabyte/llvm-project.git a2a6b655e14e7fa1fcf1011a6cb29cb8575249c0), 64-bit");

        Assert.NotNull(parsed);
        Assert.Equal(15, parsed!.Major);
        Assert.Equal(12, parsed.Minor);
    }
}
