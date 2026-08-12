using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests.infrastructure;

/// <summary>
/// Proves configured <see cref="IDatabaseContextConfiguration.MaxQueuedReads"/>/
/// <see cref="IDatabaseContextConfiguration.MaxQueuedWrites"/> values actually reach the
/// <see cref="PoolGovernor"/> instances backing a <see cref="DatabaseContext"/>, rather than
/// just being accepted and silently ignored.
/// </summary>
public class PoolGovernorQueueCapConfigurationTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static PoolGovernor GetGovernor(DatabaseContext context, string fieldName)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, AnyInstance);
        Assert.NotNull(field);
        var governor = field!.GetValue(context) as PoolGovernor;
        Assert.NotNull(governor);
        return governor!;
    }

    [Fact]
    public async System.Threading.Tasks.Task MaxQueuedWrites_Configured_ReachesWriterGovernor()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            MaxQueuedWrites = 7
        };

        await using var context = new DatabaseContext(config, factory);
        var writerGovernor = GetGovernor(context, "_writerGovernor");

        Assert.Equal(7, writerGovernor.MaxQueueDepth);
    }

    [Fact]
    public async System.Threading.Tasks.Task MaxQueuedReads_Configured_ReachesReaderGovernor()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            MaxQueuedReads = 11
        };

        await using var context = new DatabaseContext(config, factory);
        var readerGovernor = GetGovernor(context, "_readerGovernor");

        Assert.Equal(11, readerGovernor.MaxQueueDepth);
    }

    [Fact]
    public async System.Threading.Tasks.Task MaxQueuedWrites_NotConfigured_UsesGovernorDefault()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard
            // MaxQueuedWrites left at its null default — must not change today's behavior.
        };

        await using var context = new DatabaseContext(config, factory);
        var writerGovernor = GetGovernor(context, "_writerGovernor");

        // Default is maxSlots * 8, floor 32 (see PoolGovernor's own default) — just assert
        // it's a sane positive value, not the specific constant, so this doesn't couple to
        // PoolGovernor's internal default formula.
        Assert.True(writerGovernor.MaxQueueDepth >= 32);
    }
}
