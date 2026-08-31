// Targets ApplyPoolDiscriminator, StripPoolingSetting, and EnsureMinimumPoolSize - three
// public/internal methods on ConnectionPoolingConfiguration with zero existing test coverage
// per a fresh cobertura report, plus IsPoolingDisabled's untested non-zero numeric-string branch.

using System.Data.Common;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class ConnectionPoolingConfigurationUncoveredBranchTests
{
    // ── IsPoolingDisabled: numeric string, non-zero ─────────────────────────

    [Fact]
    public void IsPoolingDisabled_PoolingNonZeroNumericString_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = "Pooling=5" };
        Assert.False(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    // Note: IsPoolingDisabled's `case bool boolValue:` arm (the literal boxed-bool switch case,
    // distinct from the string "true"/"false" path below) could not be reached here either.
    // Setting DbConnectionStringBuilder's indexer directly with a real `bool` (builder["Pooling"] = false)
    // was tried, expecting it to preserve the CLR type - but DbConnectionStringBuilder normalizes
    // the stored value to a string regardless of how it's set, so TryGetValue still returns a
    // string and the switch falls through to `case string stringValue:` instead. This matches
    // this file's own IsPoolingDisabled_BoolFalse_ReturnsTrue/BoolTrue_ReturnsFalse tests, whose
    // comment already documented this as reachable only via a custom DbConnectionStringBuilder
    // subclass overriding the indexer - not attempted here, structurally hard rather than dead.

    // ── ApplyPoolDiscriminator ───────────────────────────────────────────────

    [Fact]
    public void ApplyPoolDiscriminator_NullSettingName_ReturnsUnchanged()
    {
        var cs = "Data Source=x;Initial Catalog=y";
        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(cs, null, "reader");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyPoolDiscriminator_NullSettingValue_ReturnsUnchanged()
    {
        var cs = "Data Source=x;Initial Catalog=y";
        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(cs, "Application Name", null);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyPoolDiscriminator_EmptyConnectionString_ReturnsUnchanged()
    {
        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(string.Empty, "Application Name", "reader");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ApplyPoolDiscriminator_RawConnectionString_ReturnsUnchanged()
    {
        var cs = ":memory:";
        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(cs, "Application Name", "reader");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyPoolDiscriminator_KeyAlreadyPresent_ReturnsUnchanged()
    {
        var cs = "Data Source=x;Application Name=already-set";
        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(cs, "Application Name", "reader");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyPoolDiscriminator_AddsDiscriminatorKey_WhenAbsent()
    {
        var cs = "Data Source=x";
        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(cs, "Application Name", "reader");
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.True(builder.TryGetValue("Application Name", out var value));
        Assert.Equal("reader", value);
    }

    [Fact]
    public void ApplyPoolDiscriminator_StrippingBuilder_PreservesCredentials()
    {
        var cs = "Data Source=x;Password=secret";
        var builder = new PasswordStrippingBuilder(cs);
        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(cs, "Application Name", "reader", builder);
        var resultBuilder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("secret", resultBuilder["Password"]);
        Assert.Equal("reader", resultBuilder["Application Name"]);
    }

    // ── StripPoolingSetting ──────────────────────────────────────────────────

    [Fact]
    public void StripPoolingSetting_EmptyConnectionString_ReturnsUnchanged()
    {
        var result = ConnectionPoolingConfiguration.StripPoolingSetting(string.Empty, "Pooling");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void StripPoolingSetting_RawConnectionString_ReturnsUnchanged()
    {
        var cs = ":memory:";
        var result = ConnectionPoolingConfiguration.StripPoolingSetting(cs, "Pooling");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void StripPoolingSetting_RemovesNamedSetting_WhenPresent()
    {
        var cs = "Data Source=x;Pooling=true";
        var result = ConnectionPoolingConfiguration.StripPoolingSetting(cs, "Pooling");
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.False(builder.ContainsKey("Pooling"));
    }

    [Fact]
    public void StripPoolingSetting_AlsoRemovesDefaultPoolingKey_WhenSettingNameDiffers()
    {
        // poolingSettingName is a provider-specific key distinct from the generic "Pooling"
        // fallback; both must be stripped if both happen to be present.
        var cs = "Data Source=x;Pooling Enabled=true;Pooling=true";
        var result = ConnectionPoolingConfiguration.StripPoolingSetting(cs, "Pooling Enabled");
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.False(builder.ContainsKey("Pooling Enabled"));
        Assert.False(builder.ContainsKey("Pooling"));
    }

    [Fact]
    public void StripPoolingSetting_NothingToRemove_ReturnsUnchanged()
    {
        var cs = "Data Source=x";
        var result = ConnectionPoolingConfiguration.StripPoolingSetting(cs, "Pooling");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void StripPoolingSetting_NullSettingName_StillRemovesDefaultPoolingKey()
    {
        var cs = "Data Source=x;Pooling=true";
        var result = ConnectionPoolingConfiguration.StripPoolingSetting(cs, null);
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.False(builder.ContainsKey("Pooling"));
    }

    // ── EnsureMinimumPoolSize ────────────────────────────────────────────────

    [Fact]
    public void EnsureMinimumPoolSize_NullSettingName_ReturnsUnchanged()
    {
        var cs = "Data Source=x";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, null, null, null, 5);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void EnsureMinimumPoolSize_EmptyConnectionString_ReturnsUnchanged()
    {
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(string.Empty, "Min Pool Size", null, null, 5);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void EnsureMinimumPoolSize_NegativeRequiredMinimum_ReturnsUnchanged()
    {
        var cs = "Data Source=x";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, "Min Pool Size", null, null, -1);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void EnsureMinimumPoolSize_ZeroRequired_NoExistingRawMin_ReturnsUnchanged()
    {
        var cs = "Data Source=x";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, "Min Pool Size", null, null, 0);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void EnsureMinimumPoolSize_RawMinAlreadyMeetsTarget_ReturnsUnchanged()
    {
        var cs = "Data Source=x;Min Pool Size=10";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, "Min Pool Size", 10, null, 5);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void EnsureMinimumPoolSize_RawMinBelowRequired_RaisesToRequiredMinimum()
    {
        var cs = "Data Source=x;Min Pool Size=1";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, "Min Pool Size", 1, null, 5);
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("5", builder["Min Pool Size"]!.ToString());
    }

    [Fact]
    public void EnsureMinimumPoolSize_TargetClampedToRawMax()
    {
        var cs = "Data Source=x;Min Pool Size=1;Max Pool Size=3";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, "Min Pool Size", 1, 3, 10);
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("3", builder["Min Pool Size"]!.ToString());
    }

    [Fact]
    public void EnsureMinimumPoolSize_NoExistingRawMin_AddsRequiredMinimum()
    {
        var cs = "Data Source=x";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, "Min Pool Size", null, null, 5);
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("5", builder["Min Pool Size"]!.ToString());
    }

    [Fact]
    public void EnsureMinimumPoolSize_RawConnectionString_ReturnsUnchanged()
    {
        var cs = ":memory:";
        var result = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(cs, "Min Pool Size", null, null, 5);
        Assert.Equal(cs, result);
    }
}
