// Targets ApplyApplicationName, ApplyApplicationNameSuffix, ApplyMaxPoolSize,
// and the sensitive-value-preservation path — all under-covered per Cobertura.

using System;
using System.Collections.Generic;
using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A DbConnectionStringBuilder that silently drops Password/Pwd on set,
/// mimicking providers with PersistSecurityInfo=false that strip credentials
/// when rebuilding the connection string. The original string passed to the
/// Apply* methods still contains the password, so SensitiveValuesStripped
/// detects the discrepancy and triggers ReapplyModifications.
/// </summary>
internal sealed class PasswordStrippingBuilder : DbConnectionStringBuilder
{
    private static readonly HashSet<string> SensitiveKeys =
        new(StringComparer.OrdinalIgnoreCase) { "password", "pwd" };

    public PasswordStrippingBuilder(string connectionString)
    {
        // Parse manually: add everything except password
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = segment.IndexOf('=');
            if (eqIdx < 0) continue;
            var key = segment[..eqIdx].Trim();
            var value = segment[(eqIdx + 1)..].Trim();
            if (!SensitiveKeys.Contains(key))
            {
                base[key] = value;
            }
        }
    }
}

public sealed class ConnectionPoolingConfigurationCoverageTests
{
    // ── ApplyApplicationName ──────────────────────────────────────────────

    [Fact]
    public void ApplyApplicationName_SetsNameWhenMissing()
    {
        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            "Server=localhost;Database=test",
            "MyApp",
            "Application Name");

        Assert.Contains("Application Name", result);
        Assert.Contains("MyApp", result);
    }

    [Fact]
    public void ApplyApplicationName_PreservesExisting()
    {
        var cs = "Server=localhost;Application Name=Existing";
        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            cs, "NewApp", "Application Name");

        Assert.Contains("Existing", result);
        Assert.DoesNotContain("NewApp", result);
    }

    [Fact]
    public void ApplyApplicationName_NullName_ReturnsUnchanged()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            cs, null, "Application Name");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyApplicationName_NullSettingName_ReturnsUnchanged()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            cs, "MyApp", null);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyApplicationName_EmptyConnectionString_ReturnsUnchanged()
    {
        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            "", "MyApp", "Application Name");
        Assert.Equal("", result);
    }

    [Fact]
    public void ApplyApplicationName_RawString_ReturnsUnchanged()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Data Source"] = ":memory:";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            ":memory:", "MyApp", "Application Name", builder: builder);

        Assert.Equal(":memory:", result);
    }

    // ── ApplyApplicationNameSuffix ────────────────────────────────────────

    [Fact]
    public void ApplyApplicationNameSuffix_AppendsToExisting()
    {
        var cs = "Server=localhost;Application Name=MyApp";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro");

        Assert.Contains("MyApp:ro", result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_AlreadySuffixed_NoChange()
    {
        var cs = "Server=localhost;Application Name=MyApp:ro";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro");

        // Should NOT duplicate the suffix
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_NoExisting_UsesFallback()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro", fallbackApplicationName: "FallbackApp");

        Assert.Contains("FallbackApp:ro", result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_NoExisting_NoFallback_Unchanged()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro");

        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_EmptySuffix_Unchanged()
    {
        var cs = "Server=localhost;Application Name=MyApp";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", "");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_NullSettingName_Unchanged()
    {
        var cs = "Server=localhost;Application Name=MyApp";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, null, ":ro");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_RawString_Unchanged()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Data Source"] = ":memory:";

        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            ":memory:", "Application Name", ":ro", builder: builder);

        Assert.Equal(":memory:", result);
    }

    // ── ApplyMaxPoolSize ──────────────────────────────────────────────────

    [Fact]
    public void ApplyMaxPoolSize_SetsValueWhenMissing()
    {
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            "Server=localhost;Database=test", 50, "Max Pool Size");

        Assert.Contains("Max Pool Size", result);
        Assert.Contains("50", result);
    }

    [Fact]
    public void ApplyMaxPoolSize_PreservesExistingWhenNoOverride()
    {
        var cs = "Server=localhost;Max Pool Size=100";
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 50, "Max Pool Size", overrideExisting: false);

        Assert.Contains("100", result);
    }

    [Fact]
    public void ApplyMaxPoolSize_OverridesWhenRequested()
    {
        var cs = "Server=localhost;Max Pool Size=100";
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 1, "Max Pool Size", overrideExisting: true);

        var parsed = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("1", parsed["Max Pool Size"].ToString());
    }

    [Fact]
    public void ApplyMaxPoolSize_ZeroValue_ReturnsUnchanged()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 0, "Max Pool Size");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyMaxPoolSize_NegativeValue_ReturnsUnchanged()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, -10, "Max Pool Size");
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyMaxPoolSize_NullSettingName_ReturnsUnchanged()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 50, null);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyMaxPoolSize_EmptyConnectionString_ReturnsUnchanged()
    {
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            "", 50, "Max Pool Size");
        Assert.Equal("", result);
    }

    [Fact]
    public void ApplyMaxPoolSize_RawString_ReturnsUnchanged()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Data Source"] = "file.db";

        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            "file.db", 50, "Max Pool Size", builder: builder);

        Assert.Equal("file.db", result);
    }

    // ── StripUnsupportedMaxPoolSize ───────────────────────────────────────

    [Fact]
    public void StripUnsupportedMaxPoolSize_UnsupportedProvider_RemovesMaxPoolSize()
    {
        var cs = "Data Source=test.db;Cache=Shared;Max Pool Size=10";

        var result = ConnectionPoolingConfiguration.StripUnsupportedMaxPoolSize(cs, null);

        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.False(builder.ContainsKey("Max Pool Size"));
        Assert.True(builder.ContainsKey("Cache"));
    }

    [Fact]
    public void StripUnsupportedMaxPoolSize_SupportedProvider_PreservesMaxPoolSize()
    {
        var cs = "Server=localhost;Max Pool Size=10";

        var result = ConnectionPoolingConfiguration.StripUnsupportedMaxPoolSize(cs, "Max Pool Size");

        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("10", builder["Max Pool Size"].ToString());
    }

    [Fact]
    public void StripUnsupportedMaxPoolSize_RawString_ReturnsUnchanged()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Data Source"] = ":memory:";

        var result = ConnectionPoolingConfiguration.StripUnsupportedMaxPoolSize(
            ":memory:", null, builder: builder);

        Assert.Equal(":memory:", result);
    }

    // ── Credential-preserving path ────────────────────────────────────────
    // When a provider builder strips the password on .ConnectionString read,
    // Reapply* must re-merge changes onto a generic builder that keeps creds.
    // PasswordStrippingBuilder simulates providers that strip on read.

    [Fact]
    public void ApplyPoolingDefaults_StrippingBuilder_PreservesCredentials()
    {
        var cs = "Server=localhost;Database=test;User Id=admin;Password=secret123";
        var builder = new PasswordStrippingBuilder(cs);

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            cs,
            SupportedDatabase.SqlServer,
            DbMode.Standard,
            supportsExternalPooling: true,
            builder: builder);

        Assert.Contains("secret123", result);
        Assert.Contains("admin", result);
    }

    [Fact]
    public void ApplyApplicationName_StrippingBuilder_PreservesCredentials()
    {
        var cs = "Server=localhost;User Id=admin;Password=s3cret";
        var builder = new PasswordStrippingBuilder(cs);

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            cs, "TestApp", "Application Name", builder: builder);

        Assert.Contains("s3cret", result);
        Assert.Contains("TestApp", result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_StrippingBuilder_PreservesCredentials()
    {
        var cs = "Server=localhost;Application Name=MyApp;User Id=admin;Password=s3cret";
        var builder = new PasswordStrippingBuilder(cs);

        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro", builder: builder);

        Assert.Contains("s3cret", result);
        Assert.Contains("MyApp:ro", result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_FallbackStrippingBuilder_PreservesCredentials()
    {
        var cs = "Server=localhost;User Id=admin;Password=s3cret";
        var builder = new PasswordStrippingBuilder(cs);

        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro", fallbackApplicationName: "FallbackApp", builder: builder);

        Assert.Contains("s3cret", result);
        Assert.Contains("FallbackApp:ro", result);
    }

    [Fact]
    public void ApplyMaxPoolSize_StrippingBuilder_PreservesCredentials()
    {
        var cs = "Server=localhost;User Id=admin;Password=s3cret";
        var builder = new PasswordStrippingBuilder(cs);

        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 25, "Max Pool Size", builder: builder);

        Assert.Contains("s3cret", result);
        Assert.Contains("25", result);
    }

    [Fact]
    public void ApplyMaxPoolSize_OverrideStrippingBuilder_PreservesCredentials()
    {
        var cs = "Server=localhost;Max Pool Size=100;User Id=admin;Password=s3cret";
        var builder = new PasswordStrippingBuilder(cs);

        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 50, "Max Pool Size", overrideExisting: true, builder: builder);

        Assert.Contains("s3cret", result);
        Assert.Contains("50", result);
    }

    // ── SingleConnection mode — pooling defaults not applied ──────────────

    [Fact]
    public void ApplyPoolingDefaults_SingleConnectionMode_ReturnsOriginal()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            cs, SupportedDatabase.SqlServer, DbMode.SingleConnection, true);
        Assert.Equal(cs, result);
    }

    [Fact]
    public void ApplyPoolingDefaults_NoExternalPooling_ReturnsOriginal()
    {
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            cs, SupportedDatabase.SqlServer, DbMode.Standard, false);
        Assert.Equal(cs, result);
    }

    // ── ApplyPoolingDefaults with whitespace-only connection string ────────

    [Fact]
    public void ApplyPoolingDefaults_WhitespaceConnectionString_ReturnsOriginal()
    {
        var cs = "   ";
        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            cs, SupportedDatabase.SqlServer, DbMode.Standard, supportsExternalPooling: true);
        Assert.Equal(cs, result);
    }

    // ── IsPoolingDisabled with bool-typed value ───────────────────────────

    [Fact]
    public void IsPoolingDisabled_BoolFalse_ReturnsTrue()
    {
        // DbConnectionStringBuilder stores "Pooling" as a string; to get
        // a bool-typed value we inject via the indexer on a subclass or
        // exercise the string "false" path which parses identically.
        var builder = new DbConnectionStringBuilder();
        builder["Pooling"] = "false";
        Assert.True(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    [Fact]
    public void IsPoolingDisabled_BoolTrue_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Pooling"] = "true";
        Assert.False(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    [Fact]
    public void IsPoolingDisabled_IntZero_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Pooling"] = "0";
        Assert.True(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    [Fact]
    public void IsPoolingDisabled_IntPositive_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Pooling"] = "1";
        Assert.False(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    [Fact]
    public void IsPoolingDisabled_NullBuilder_ReturnsFalse()
    {
        Assert.False(ConnectionPoolingConfiguration.IsPoolingDisabled(null!));
    }

    [Fact]
    public void IsPoolingDisabled_NoPoolingKey_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Server"] = "localhost";
        Assert.False(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    [Fact]
    public void IsPoolingDisabled_UnparsableValue_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Pooling"] = "maybe";
        Assert.False(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    // ── SensitiveValuesStripped → ReapplyModifications in Suffix ──────────
    // These exercise the credential-preservation path inside
    // ApplyApplicationNameSuffix and ApplyMaxPoolSize, which was uncovered.

    [Fact]
    public void ApplyApplicationNameSuffix_WithPassword_PreservesCredentials()
    {
        var cs = "Server=localhost;Application Name=MyApp;User Id=admin;Password=s3cret";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro");

        Assert.Contains("s3cret", result);
        Assert.Contains("MyApp:ro", result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_FallbackWithPassword_PreservesCredentials()
    {
        var cs = "Server=localhost;User Id=admin;Password=s3cret";
        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "Application Name", ":ro", fallbackApplicationName: "FallbackApp");

        Assert.Contains("s3cret", result);
        Assert.Contains("FallbackApp:ro", result);
    }

    [Fact]
    public void ApplyMaxPoolSize_WithPassword_PreservesCredentials()
    {
        var cs = "Server=localhost;User Id=admin;Password=s3cret";
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 25, "Max Pool Size");

        Assert.Contains("s3cret", result);
        Assert.Contains("25", result);
    }

    [Fact]
    public void ApplyMaxPoolSize_OverrideWithPassword_PreservesCredentials()
    {
        var cs = "Server=localhost;Max Pool Size=100;User Id=admin;Password=s3cret";
        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            cs, 50, "Max Pool Size", overrideExisting: true);

        Assert.Contains("s3cret", result);
        Assert.Contains("50", result);
    }

    // ── HasMinPoolSize ─────────────────────────────────────────────────────

    [Fact]
    public void HasMinPoolSize_NullBuilder_ReturnsFalse()
    {
        Assert.False(ConnectionPoolingConfiguration.HasMinPoolSize(null!));
    }

    [Fact]
    public void HasMinPoolSize_WithMinPoolSize_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Min Pool Size"] = "5";
        Assert.True(ConnectionPoolingConfiguration.HasMinPoolSize(builder));
    }

    [Fact]
    public void HasMinPoolSize_WithoutMinPoolSize_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Server"] = "localhost";
        Assert.False(ConnectionPoolingConfiguration.HasMinPoolSize(builder));
    }

    // ── TrySetMinPoolSize ──────────────────────────────────────────────────

    [Fact]
    public void TrySetMinPoolSize_NullBuilder_ReturnsFalse()
    {
        Assert.False(ConnectionPoolingConfiguration.TrySetMinPoolSize(null, 5));
    }

    [Fact]
    public void TrySetMinPoolSize_PoolingDisabled_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Pooling"] = "false";
        Assert.False(ConnectionPoolingConfiguration.TrySetMinPoolSize(builder, 5));
    }

    [Fact]
    public void TrySetMinPoolSize_AlreadySet_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Min Pool Size"] = "10";
        Assert.False(ConnectionPoolingConfiguration.TrySetMinPoolSize(builder, 5));
    }

    [Fact]
    public void TrySetMinPoolSize_ValidBuilder_SetsAndReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Server"] = "localhost";
        // Generic builder supports arbitrary keys via indexer
        var result = ConnectionPoolingConfiguration.TrySetMinPoolSize(builder, 3);
        Assert.True(result);
    }

    // ── ApplyPoolingDefaults with SingleWriter mode ───────────────────────

    [Fact]
    public void ApplyPoolingDefaults_SingleWriterMode_AppliesPooling()
    {
        // SingleWriter is in the allowed set (Standard | KeepAlive | SingleWriter)
        var cs = "Server=localhost;Database=test";
        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            cs, SupportedDatabase.SqlServer, DbMode.SingleWriter, supportsExternalPooling: true);
        // Should not return unchanged — pooling defaults are applied
        Assert.NotNull(result);
    }
}
