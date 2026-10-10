using System;
using System.Text.RegularExpressions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A provider pool's Maximum Pool Size is a hard cap on its connections, in use or idle, so a governor
/// that admits more callers than its pool can serve makes the extra ones wait inside the provider for
/// the provider's own timeout instead of under PoolAcquireTimeout. When the clamp splits the server's
/// budget across the two role pools, each governor must therefore be sized to its pool, and the two
/// pools together must fit the server.
/// </summary>
public sealed class ServerCeilingGovernorAgreementTests
{
    private const string ConnectionString = "Host=db1;Database=d;Username=u;Password=p";
    private const int UsableLimit = 22; // max_connections 25, 3 held back for superusers

    private static DatabaseContext Create(
        Action<DatabaseContextConfiguration> configure,
        SupportedDatabase product = SupportedDatabase.PostgreSql)
    {
        var factory = new fakeDbFactory(product);

        // A server answers the same on every connection; some modes use the first one as a sentinel, so
        // the probe may be served by a later one.
        for (var i = 0; i < 3; i++)
        {
            var connection = new fakeDbConnection();
            connection.ScalarResultsByCommand["SHOW max_connections"] = "25";
            connection.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
            connection.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
            factory.Connections.Add(connection);
        }

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = ConnectionString,
            DbMode = DbMode.Standard,
            EnableMetrics = true,
            ClampPoolsToServerConnectionLimit = true
        };
        configure(config);
        return new DatabaseContext(config, factory);
    }

    private static (int Reader, int Writer) Slots(DatabaseContext context) =>
        (context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots,
            context.GetPoolStatisticsSnapshot(PoolLabel.Writer).MaxSlots);

    private static int? Setting(string connectionString, string name)
    {
        var match = Regex.Match(connectionString, $"{Regex.Escape(name)}=(\\d+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    [Fact]
    public void WhenTheBudgetIsSplit_EachGovernorIsSizedToItsProviderPool()
    {
        using var context = Create(c =>
        {
            c.MaxConcurrentReads = 20;
            c.MaxConcurrentWrites = 20;
        });
        using var write = context.GetConnection(ExecutionType.Write);
        using var read = context.GetConnection(ExecutionType.Read);

        var (readerSlots, writerSlots) = Slots(context);

        Assert.Equal(Setting(read.ConnectionString, "Maximum Pool Size"), readerSlots);
        Assert.Equal(Setting(write.ConnectionString, "Maximum Pool Size"), writerSlots);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(20, 20)]
    [InlineData(30, 10)]
    [InlineData(40, 1)]
    [InlineData(1, 40)]
    public void TheTwoRolesTogether_NeverAdmitMoreThanTheServersUsableLimit(int reads, int writes)
    {
        using var context = Create(c =>
        {
            c.MaxConcurrentReads = reads;
            c.MaxConcurrentWrites = writes;
        });

        var (reader, writer) = Slots(context);

        Assert.InRange(reader + writer, 2, UsableLimit);
    }

    [Fact]
    public void WithNothingRequested_TheTwoRolesTogetherStillFitTheServer()
    {
        using var context = Create(_ => { });

        var (reader, writer) = Slots(context);

        Assert.InRange(reader + writer, 2, UsableLimit);
    }

    [Fact]
    public void AReadConnectionStringOnTheSameServerButAnotherUser_IsBudgetedWithTheWriter()
    {
        using var context = Create(c =>
            c.ReadOnlyConnectionString = "Host=db1;Database=d;Username=reader;Password=p");

        var (reader, writer) = Slots(context);

        Assert.InRange(reader + writer, 2, UsableLimit);
    }

    [Fact]
    public void AForbiddenReadPool_StaysForbiddenWhenTheClampIsOn()
    {
        using var context = Create(c => c.MaxConcurrentReads = 0);

        Assert.Equal(0, Slots(context).Reader);
        Assert.Throws<PoolForbiddenException>(() => context.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public void AMinimumPoolSizeAboveTheClampedMaximum_IsLoweredSoTheProviderAcceptsTheString()
    {
        using var context = Create(c =>
        {
            c.ConnectionString = ConnectionString + ";Minimum Pool Size=20";
            c.MaxConcurrentReads = 20;
            c.MaxConcurrentWrites = 20;
        });
        using var write = context.GetConnection(ExecutionType.Write);

        var min = Setting(write.ConnectionString, "Minimum Pool Size");
        var max = Setting(write.ConnectionString, "Maximum Pool Size");

        Assert.NotNull(max);
        Assert.True(min is null || min <= max, $"Minimum Pool Size {min} exceeds Maximum Pool Size {max}");
    }

    [Fact]
    public void PreventDatabaseUnload_WithTheClampOn_StillReadsTheServersLimit()
    {
        using var context = Create(c => c.DbMode = DbMode.PreventDatabaseUnload);

        var (reader, writer) = Slots(context);

        Assert.InRange(reader + writer, 2, UsableLimit);
    }
}
