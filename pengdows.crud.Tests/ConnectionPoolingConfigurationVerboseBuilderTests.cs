// Reproduces a real Db2 production regression: IBM.Data.Db2's DB2ConnectionStringBuilder
// unconditionally re-serializes its ENTIRE known property schema (~90 keys, most empty)
// from its ConnectionString getter, regardless of which keys were actually set. When
// ConnectionPoolingConfiguration trusted that getter's output as the final connection
// string, the result ballooned into a string containing dozens of phantom keys nobody
// asked for (e.g. "IsolationLevel=;CurrentSchema=;...") — which the real DB2 driver's
// native connection-string parser then rejected outright with
// "System.ArgumentException: Invalid argument" at DB2ConnPool.ReplaceConnectionStringParms,
// on every single connection open. VerboseSerializingBuilder below reproduces the same
// "echoes far more than was set" shape without depending on the real Db2 driver package.

using System;
using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A DbConnectionStringBuilder that mimics a provider whose internal property bag always
/// carries its full known schema (many phantom, empty-valued keys nobody set), as
/// IBM.Data.Db2's DB2ConnectionStringBuilder does — its typed constructor seeds every
/// schema-defined keyword into the internal collection up front, so the base (non-virtual)
/// ConnectionString getter later echoes all of them back, not just what pengdows.crud set.
/// </summary>
internal sealed class VerboseSerializingBuilder : DbConnectionStringBuilder
{
    public VerboseSerializingBuilder(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = segment.IndexOf('=');
            if (eqIdx < 0)
            {
                continue;
            }

            var key = segment[..eqIdx].Trim();
            var value = segment[(eqIdx + 1)..].Trim();
            base[key] = value;
        }

        base["PhantomIsolationLevel"] = string.Empty;
        base["PhantomCurrentSchema"] = string.Empty;
        base["PhantomIsA90KeySchema"] = "False";
    }
}

public sealed class ConnectionPoolingConfigurationVerboseBuilderTests
{
    private const string BaseConnectionString = "Server=localhost;Database=testdb;Uid=db2inst1;Pwd=MyStr0ngP@ssw0rd";

    [Fact]
    public void ApplyMaxPoolSize_VerboseBuilder_DoesNotLeakPhantomKeys()
    {
        var builder = new VerboseSerializingBuilder(BaseConnectionString);

        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            BaseConnectionString, 5, "Max Pool Size", overrideExisting: true, builder: builder);

        Assert.DoesNotContain("Phantom", result);
        Assert.Contains("Max Pool Size=5", result);
        Assert.Contains("MyStr0ngP@ssw0rd", result);
    }

    [Fact]
    public void ApplyApplicationName_VerboseBuilder_DoesNotLeakPhantomKeys()
    {
        var builder = new VerboseSerializingBuilder(BaseConnectionString);

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            BaseConnectionString, "MyApp", "ClientApplicationName", builder: builder);

        Assert.DoesNotContain("Phantom", result);
        Assert.Contains("ClientApplicationName=MyApp", result);
    }

    [Fact]
    public void ApplyPoolingDefaults_VerboseBuilder_DoesNotLeakPhantomKeys()
    {
        var builder = new VerboseSerializingBuilder(BaseConnectionString);

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            BaseConnectionString,
            SupportedDatabase.Db2,
            DbMode.Standard,
            supportsExternalPooling: true,
            builder: builder);

        Assert.DoesNotContain("Phantom", result);
        Assert.Contains("Pooling=True", result);
    }

    [Fact]
    public void ApplyPoolDiscriminator_VerboseBuilder_DoesNotLeakPhantomKeys()
    {
        var builder = new VerboseSerializingBuilder(BaseConnectionString);

        var result = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(
            BaseConnectionString, "SomeDiscriminator", "SomeValue", builder: builder);

        Assert.DoesNotContain("Phantom", result);
        Assert.Contains("SomeDiscriminator=SomeValue", result);
    }

    [Fact]
    public void ApplyApplicationNameSuffix_VerboseBuilder_DoesNotLeakPhantomKeys()
    {
        var cs = BaseConnectionString + ";ClientApplicationName=MyApp";
        var builder = new VerboseSerializingBuilder(cs);

        var result = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            cs, "ClientApplicationName", "-ro", builder: builder);

        Assert.DoesNotContain("Phantom", result);
        // ADO.NET connection string keys are case-insensitive; a plain DbConnectionStringBuilder
        // normalizes keys it parsed from the original text to lowercase (matching what the real
        // typed Db2 builder already did for its own non-strongly-typed keys in the original bug).
        Assert.Contains("MyApp-ro", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clientapplicationname=MyApp-ro", result, StringComparison.OrdinalIgnoreCase);
    }
}
