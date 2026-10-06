using System;
using System.Reflection;
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
        // A DateTime (and a DateTimeOffset bound for a column with no offset) is a UTC wall time, so
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

    [Fact]
    public void SnowflakeDialect_SessionSettings_PublicApis_UseCanonicalScript()
    {
        var dialect = CreateDialect();
        var cacheField = typeof(SnowflakeDialect).GetField("_sessionSettings",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(cacheField);
        cacheField!.SetValue(dialect, "ALTER SESSION SET TIMEZONE = 'Mars';");

        var baseSettings = dialect.GetBaseSessionSettings();
        var readOnlySettings = dialect.GetFinalSessionSettings(readOnly: true);
        var readWriteSettings = dialect.GetFinalSessionSettings(readOnly: false);

        Assert.Equal(readOnlySettings, baseSettings);
        Assert.Equal(readWriteSettings, baseSettings);
        Assert.Contains("CLIENT_TIMESTAMP_TYPE_MAPPING = TIMESTAMP_NTZ", baseSettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TIMEZONE = 'Mars'", baseSettings, StringComparison.OrdinalIgnoreCase);
    }

    // ─── TIME binding ─────────────────────────────────────────────────────────

    // Snowflake.Data binds DbType.Time only from a DateTime and sends its time of day
    // (SFDataConverter.CSharpValToSfVal, TIME branch, 5.6.0); a TimeSpan fails live with
    // "Failed to convert data 13:45:30 from type System.TimeSpan to type Time" (270003). TYPE-001.
    [Fact]
    public void CreateDbParameter_TimeSpanAndTimeOnlyForTime_BindAsDateTimeCarryingTheTimeOfDay()
    {
        var dialect = CreateDialect();

        foreach (var value in new object[] { new TimeSpan(13, 45, 30), new TimeOnly(13, 45, 30) })
        {
            var parameter = dialect.CreateDbParameter("t", System.Data.DbType.Time, value);

            var bound = Assert.IsType<DateTime>(parameter.Value);
            Assert.Equal(new TimeSpan(13, 45, 30), bound.TimeOfDay);
            Assert.Equal(System.Data.DbType.Time, parameter.DbType);
        }
    }

    [Fact]
    public void CreateDbParameter_TimeSpanForOtherDbTypes_IsUnchanged()
    {
        var dialect = CreateDialect();

        var parameter = dialect.CreateDbParameter("t", System.Data.DbType.Object, new TimeSpan(1, 2, 3));

        Assert.Equal(new TimeSpan(1, 2, 3), parameter.Value);
    }
}
