using System;
using System.Text.RegularExpressions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// How each pool's size is decided, from the highest-priority source to the lowest. This is the order
/// docs/connection-pooling.md states, pinned here so the documentation cannot drift from the code.
///
///   1. MaxConcurrentReads / MaxConcurrentWrites
///   2. the pool-size setting in that pool's connection string (Max Pool Size and its spellings)
///   3. the dialect's default (100 unless the dialect says otherwise)
///
/// Then, applied to whatever won: the absolute ceiling of 512; the mode rules (SingleWriter's one writer,
/// PreventDatabaseUnload's floor of 2); and, only when opted into, the server's own connection limit.
/// The size that wins is used for the governor and written into the provider's connection string, so the
/// two always agree.
/// </summary>
public sealed class PoolSizeResolutionOrderTests
{
    private const string Base = "Host=db1;Database=d;Username=u;Password=p";

    private static DatabaseContext Create(
        string connectionString = Base,
        Action<DatabaseContextConfiguration>? configure = null,
        SupportedDatabase product = SupportedDatabase.PostgreSql)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Standard
        };
        configure?.Invoke(config);
        return new DatabaseContext(config, new fakeDbFactory(product));
    }

    private static int Slots(DatabaseContext context, PoolLabel label) =>
        context.GetPoolStatisticsSnapshot(label).MaxSlots;

    private static int? ProviderMax(DatabaseContext context, ExecutionType type)
    {
        using var connection = context.GetConnection(type);
        var match = Regex.Match(connection.ConnectionString, @"Maximum Pool Size=(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    // ── the three sources, in order ──

    [Fact]
    public void WithNothingSet_TheDialectsDefaultIsUsed()
    {
        using var context = Create();

        Assert.Equal(100, Slots(context, PoolLabel.Reader));
        Assert.Equal(100, Slots(context, PoolLabel.Writer));
        Assert.Equal(100, ProviderMax(context, ExecutionType.Read));
        Assert.Equal(100, ProviderMax(context, ExecutionType.Write));
    }

    [Fact]
    public void ThePoolSizeInTheConnectionString_BeatsTheDialectDefault()
    {
        using var context = Create(Base + ";Maximum Pool Size=30");

        Assert.Equal(30, Slots(context, PoolLabel.Reader));
        Assert.Equal(30, Slots(context, PoolLabel.Writer));
        Assert.Equal(30, ProviderMax(context, ExecutionType.Write));
    }

    [Theory]
    [InlineData("Max Pool Size=30")]
    [InlineData("MaxPoolSize=30")]
    [InlineData("Maximum Pool Size=30")]
    public void EveryProviderSpellingOfThePoolSize_IsRecognised(string setting)
    {
        using var context = Create(Base + ";" + setting);

        Assert.Equal(30, Slots(context, PoolLabel.Writer));
    }

    [Fact]
    public void Configuration_BeatsTheConnectionString_AndTheProviderStringIsRewrittenToMatch()
    {
        using var context = Create(Base + ";Maximum Pool Size=30", c =>
        {
            c.MaxConcurrentReads = 10;
            c.MaxConcurrentWrites = 7;
        });

        Assert.Equal(10, Slots(context, PoolLabel.Reader));
        Assert.Equal(7, Slots(context, PoolLabel.Writer));
        Assert.Equal(10, ProviderMax(context, ExecutionType.Read));
        Assert.Equal(7, ProviderMax(context, ExecutionType.Write));
    }

    [Fact]
    public void Configuration_BeatsTheDialectDefault()
    {
        using var context = Create(configure: c => c.MaxConcurrentReads = 10);

        Assert.Equal(10, Slots(context, PoolLabel.Reader));
        Assert.Equal(100, Slots(context, PoolLabel.Writer));
        Assert.Equal(10, ProviderMax(context, ExecutionType.Read));
        Assert.Equal(100, ProviderMax(context, ExecutionType.Write));
    }

    [Fact]
    public void EachRoleIsDecidedOnItsOwn()
    {
        using var context = Create(Base + ";Maximum Pool Size=30", c => c.MaxConcurrentReads = 10);

        Assert.Equal(10, Slots(context, PoolLabel.Reader));
        Assert.Equal(30, Slots(context, PoolLabel.Writer));
        Assert.Equal(10, ProviderMax(context, ExecutionType.Read));
        Assert.Equal(30, ProviderMax(context, ExecutionType.Write));
    }

    [Fact]
    public void AReadOnlyConnectionStringsOwnPoolSize_IsUsedForReads()
    {
        using var context = Create(Base + ";Maximum Pool Size=30", c =>
            c.ReadOnlyConnectionString = "Host=replica;Database=d;Username=u;Password=p;Maximum Pool Size=12");

        Assert.Equal(12, Slots(context, PoolLabel.Reader));
        Assert.Equal(30, Slots(context, PoolLabel.Writer));
        Assert.Equal(12, ProviderMax(context, ExecutionType.Read));
        Assert.Equal(30, ProviderMax(context, ExecutionType.Write));
    }

    // ── the rules applied to whatever won ──

    [Fact]
    public void NoSourceCanAskForMoreThanTheAbsoluteCeilingOf512()
    {
        using var fromConfiguration = Create(configure: c => c.MaxConcurrentReads = 1000);
        using var fromConnectionString = Create(Base + ";Maximum Pool Size=1000");

        Assert.Equal(512, Slots(fromConfiguration, PoolLabel.Reader));
        Assert.Equal(512, Slots(fromConnectionString, PoolLabel.Reader));
        Assert.Equal(512, Slots(fromConnectionString, PoolLabel.Writer));
    }

    [Fact]
    public void ASizeOfZeroInConfiguration_ForbidsThatPool()
    {
        using var context = Create(configure: c => c.MaxConcurrentReads = 0);

        Assert.Equal(0, Slots(context, PoolLabel.Reader));
        Assert.True(context.GetPoolStatisticsSnapshot(PoolLabel.Reader).Forbidden);
        Assert.Throws<PoolForbiddenException>(() => context.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public void AWriteSizeOfZero_MakesTheWholeContextReadOnly()
    {
        using var context = Create(configure: c => c.MaxConcurrentWrites = 0);

        Assert.True(context.GetPoolStatisticsSnapshot(PoolLabel.Writer).Forbidden);
        Assert.Equal(100, Slots(context, PoolLabel.Reader));
    }

    [Fact]
    public void SingleWriter_AlwaysHasExactlyOneWriter_WhateverTheSourcesSay()
    {
        using var context = Create(Base + ";Maximum Pool Size=30", c =>
        {
            c.DbMode = DbMode.SingleWriter;
            c.MaxConcurrentWrites = 8;
        });

        Assert.Equal(1, Slots(context, PoolLabel.Writer));
        Assert.Equal(30, Slots(context, PoolLabel.Reader));
    }

    [Fact]
    public void PreventDatabaseUnload_RaisesAPoolOfOneToTwo_ForTheSentinelAndOneWorker()
    {
        using var context = Create(configure: c =>
        {
            c.DbMode = DbMode.PreventDatabaseUnload;
            c.MaxConcurrentWrites = 1;
        });

        Assert.Equal(2, Slots(context, PoolLabel.Writer));
    }

    [Fact]
    public void ASizeOfZeroInTheConnectionString_IsTreatedAsUnset_NotAsAForbiddenPool()
    {
        // Only MaxConcurrentReads/MaxConcurrentWrites = 0 forbids a pool. A 0 in the connection string falls
        // back to the dialect default, with the opt-in server clamp on or off.
        using var plain = Create(Base + ";Maximum Pool Size=0");

        Assert.Equal(100, Slots(plain, PoolLabel.Reader));
        Assert.Equal(100, Slots(plain, PoolLabel.Writer));
        Assert.False(plain.GetPoolStatisticsSnapshot(PoolLabel.Reader).Forbidden);
    }

    [Fact]
    public void ASizeOfZeroInTheConnectionString_IsStillNotForbidden_WhenTheServerClampIsOn()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection();
        connection.ScalarResultsByCommand["SHOW max_connections"] = "25";
        connection.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        connection.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(connection);

        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = Base + ";Maximum Pool Size=0",
            DbMode = DbMode.Standard,
            ClampPoolsToServerConnectionLimit = true
        }, factory);

        Assert.False(context.GetPoolStatisticsSnapshot(PoolLabel.Reader).Forbidden);
        Assert.False(context.GetPoolStatisticsSnapshot(PoolLabel.Writer).Forbidden);
        Assert.Equal(11, Slots(context, PoolLabel.Reader));
        Assert.Equal(11, Slots(context, PoolLabel.Writer));
    }

    // ── an engine with no provider pool to size ──

    [Fact]
    public void AnEngineWithoutAProviderPool_IgnoresTheConnectionStringSize_AndIsBoundedOnlyByTheAbsoluteCeiling()
    {
        // SQLite runs in-process: there is no provider pool for a connection-string size to configure, and
        // its default is unbounded, so only the absolute ceiling (512) applies.
        using var context = Create(
            "Data Source=test.db;EmulatedProduct=Sqlite;Max Pool Size=30",
            product: SupportedDatabase.Sqlite);

        Assert.Equal(512, Slots(context, PoolLabel.Reader));
    }

    [Fact]
    public void AnEngineWithoutAProviderPool_IgnoresTheConnectionStringSize_ButHonoursConfiguration()
    {
        using var context = Create(
            "Data Source=test.db;EmulatedProduct=Sqlite;Max Pool Size=30",
            c => c.MaxConcurrentReads = 5,
            SupportedDatabase.Sqlite);

        Assert.Equal(5, Slots(context, PoolLabel.Reader));
        Assert.Equal(1, Slots(context, PoolLabel.Writer));
    }

    // ── the server's own limit, only when opted into, comes last ──

    [Fact]
    public void TheServerClamp_IsAppliedLast_ToWhateverSizeWon()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        for (var i = 0; i < 2; i++)
        {
            var connection = new fakeDbConnection();
            connection.ScalarResultsByCommand["SHOW max_connections"] = "25";
            connection.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
            connection.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
            factory.Connections.Add(connection);
        }

        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = Base + ";Maximum Pool Size=60",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 10,
            ClampPoolsToServerConnectionLimit = true
        }, factory);

        // Reads asked for 10 (configuration) and writes for 60 (connection string). Each role is first held
        // to the server's 22 usable connections on its own, so writes become 22; the two (10 + 22) still
        // exceed 22 together, so the limit is then shared out in proportion to those sizes.
        Assert.Equal(6, Slots(context, PoolLabel.Reader));
        Assert.Equal(16, Slots(context, PoolLabel.Writer));
    }
}
