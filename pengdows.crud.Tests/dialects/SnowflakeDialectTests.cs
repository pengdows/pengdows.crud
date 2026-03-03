using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Targeted tests for Snowflake-specific dialect behaviour: proc wrapping, output parameters,
/// and session settings completeness.
/// </summary>
public class SnowflakeDialectTests
{
    private static SnowflakeDialect CreateDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        return new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance);
    }

    // ─── Proc wrapping ────────────────────────────────────────────────────────

    [Fact]
    public void SnowflakeDialect_ProcWrappingStyle_IsCall()
    {
        // Snowflake stored procedures are invoked with CALL proc_name(args).
        // ProcWrappingStyle.None would throw NotSupportedException — that is wrong.
        var dialect = CreateDialect();
        Assert.Equal(ProcWrappingStyle.Call, dialect.ProcWrappingStyle);
    }

    [Fact]
    public void SnowflakeDialect_MaxOutputParameters_IsNonZero()
    {
        // Snowflake stored procedures accept parameters; MaxOutputParameters = 0 is incorrect.
        var dialect = CreateDialect();
        Assert.True(dialect.MaxOutputParameters > 0,
            $"Expected MaxOutputParameters > 0 but got {dialect.MaxOutputParameters}");
    }

    // ─── Session settings: CLIENT_TIMESTAMP_TYPE_MAPPING ─────────────────────

    [Fact]
    public void SnowflakeDialect_GetBaseSessionSettings_IncludesClientTimestampTypeMapping()
    {
        // The Snowflake .NET driver defaults to TIMESTAMP_LTZ for DateTime binding.
        // Since the dialect normalises DateTimeOffset → UTC DateTime for NTZ columns,
        // the session must explicitly set CLIENT_TIMESTAMP_TYPE_MAPPING = TIMESTAMP_NTZ
        // to avoid timezone metadata being attached at bind time.
        var dialect = CreateDialect();
        var settings = dialect.GetBaseSessionSettings();

        Assert.Contains("CLIENT_TIMESTAMP_TYPE_MAPPING", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TIMESTAMP_NTZ", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnowflakeDialect_GetFinalSessionSettings_ReadOnly_IncludesClientTimestampTypeMapping()
    {
        var dialect = CreateDialect();
        var settings = dialect.GetFinalSessionSettings(readOnly: true);

        Assert.Contains("CLIENT_TIMESTAMP_TYPE_MAPPING", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TIMESTAMP_NTZ", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnowflakeDialect_GetFinalSessionSettings_ReadWrite_IncludesClientTimestampTypeMapping()
    {
        var dialect = CreateDialect();
        var settings = dialect.GetFinalSessionSettings(readOnly: false);

        Assert.Contains("CLIENT_TIMESTAMP_TYPE_MAPPING", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TIMESTAMP_NTZ", settings, StringComparison.OrdinalIgnoreCase);
    }
}
