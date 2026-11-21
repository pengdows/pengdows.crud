using System.Text.RegularExpressions;
using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// The trace file is appended to by every run. A fixed name in the shared temp directory lets another local
/// account create it first (a symlink would redirect the appends), so each process gets its own random name.
/// </summary>
public sealed class IntegrationTraceLogTests
{
    private static readonly Regex RandomNameSuffix = new(@"-[0-9a-f]{32}\.log$", RegexOptions.CultureInvariant);

    [Fact]
    public void TracePath_IsNotTheFixedSharedName()
    {
        var path = IntegrationTraceLog.TracePath(SupportedDatabase.PostgreSql);

        Assert.NotEqual("/tmp/integration-trace.log", path);
        Assert.DoesNotContain(Path.Combine(Path.GetTempPath(), "integration-trace.log"), path);
    }

    [Fact]
    public void TracePath_CarriesARandomPerProcessSuffix()
    {
        var path = IntegrationTraceLog.TracePath(SupportedDatabase.PostgreSql);

        Assert.Matches(RandomNameSuffix, path);
    }

    [Fact]
    public void TracePath_IsStableWithinOneProcess_SoARunWritesOneFile()
    {
        Assert.Equal(
            IntegrationTraceLog.TracePath(SupportedDatabase.PostgreSql),
            IntegrationTraceLog.TracePath(SupportedDatabase.MySql));
    }

    [Fact]
    public void SnowflakeTracePath_IsRandomToo_AndDistinctFromTheGeneralTrace()
    {
        var snowflake = IntegrationTraceLog.TracePath(SupportedDatabase.Snowflake);

        Assert.Matches(RandomNameSuffix, snowflake);
        Assert.NotEqual(IntegrationTraceLog.TracePath(SupportedDatabase.PostgreSql), snowflake);
    }
}
