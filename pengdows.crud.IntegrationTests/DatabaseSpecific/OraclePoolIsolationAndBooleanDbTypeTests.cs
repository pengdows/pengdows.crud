using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Ported from testbed/Oracle/OracleTestProvider.cs's TestPoolIsolation/
/// TestBooleanDbTypeServerVersionBehavior overrides (part of the testbed-vs-IntegrationTests
/// consolidation).
/// </summary>
[Collection("IntegrationTests")]
public class OraclePoolIsolationAndBooleanDbTypeTests : DatabaseTestBase
{
    public OraclePoolIsolationAndBooleanDbTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.Oracle);

    /// <summary>
    /// Builds a fresh context with a tiny pool (Max Pool Size=1) to make pool-sharing
    /// detectable: if the reader mistakenly shares the writer's pool, it blocks on the already-
    /// occupied single slot. With separate pools (ODP.NET's Metadata Pooling discriminator), the
    /// reader opens from its own empty pool and completes immediately.
    /// </summary>
    [SkippableFact]
    public async Task PoolIsolation_ReaderAndWriter_UseSeparatePools()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async context =>
        {
            var rawCs = ((DatabaseContext)context).RawConnectionString;
            var builder = new DbConnectionStringBuilder { ConnectionString = rawCs };
            builder["Max Pool Size"] = 1;
            builder["Connection Timeout"] = 30;

            var cfg = new DatabaseContextConfiguration
            {
                ConnectionString = builder.ConnectionString,
                DbMode = DbMode.Standard,
                ReadWriteMode = ReadWriteMode.ReadWrite
            };

            await using var ctx = new DatabaseContext(cfg, OracleClientFactory.Instance);

            var writer = ctx.GetConnection(ExecutionType.Write);
            await writer.OpenAsync();
            try
            {
                var reader = ctx.GetConnection(ExecutionType.Read);
                await reader.OpenAsync();
                ctx.CloseAndDisposeConnection(reader);
            }
            finally
            {
                ctx.CloseAndDisposeConnection(writer);
            }
        });
    }

    /// <summary>
    /// FEAT-008: OracleDialect.RemapDbType converts DbType.Boolean -> DbType.Int16
    /// unconditionally, regardless of which Oracle server version is connected. This proves why
    /// that's still necessary: binds a raw (bypassing pengdows.crud's dialect/remap layer
    /// entirely) OracleParameter with DbType.Boolean against whichever server this run targets.
    /// Oracle Database 23c/23ai added a genuine native BOOLEAN type — binding succeeds there, but
    /// throws InvalidCastException against an older (18c/21c) server that predates it.
    /// </summary>
    [SkippableFact]
    public async Task BooleanDbType_BindsNativelyOn23cPlus_ThrowsInvalidCastEarlierOtherwise()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async context =>
        {
            var rawCs = ((DatabaseContext)context).RawConnectionString;
            await using var connection = new OracleConnection(rawCs);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT :p1 FROM DUAL";
            var p = cmd.CreateParameter();
            p.ParameterName = "p1";
            p.DbType = DbType.Boolean;
            p.Value = true;
            cmd.Parameters.Add(p);

            var serverIsAtLeast23c = context.DataSourceInfo.ParsedVersion?.Major >= 23;

            if (serverIsAtLeast23c)
            {
                var result = await cmd.ExecuteScalarAsync();
                Assert.True(result is true or "1" or 1,
                    $"Expected native Boolean bind to round-trip true on a 23c+ server, got {result ?? "null"}");
            }
            else
            {
                await Assert.ThrowsAsync<InvalidCastException>(() => cmd.ExecuteScalarAsync());
            }
        });
    }
}
