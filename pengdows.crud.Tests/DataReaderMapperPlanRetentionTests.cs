using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// COR-006 (found by the 2026-10-04 performance review): for a column the provider can't resolve
/// (SQL Server geometry without Microsoft.SqlServer.Types), DataReaderMapper's static plan cache kept
/// a delegate bound to the TrackedReader it first mapped, so that reader (with its command and
/// connection wrapper) stayed reachable for the life of the process.
/// </summary>
public class DataReaderMapperPlanRetentionTests
{
    public sealed class RetentionRow
    {
        public int Id { get; set; }
        public Geometry? Geom { get; set; }
    }

    private static readonly byte[] StoredGeometry = Convert.FromHexString("110F0000010C000000000000F03F0000000000000040");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> MapOnceAsync(DatabaseContext context, fakeDbConnection exec)
    {
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Id"] = 1, ["Geom"] = StoredGeometry }
        })
        {
            UnloadableUdtColumns = new Dictionary<string, string> { ["Geom"] = "master.sys.geometry" }
        });
        await using var sc = context.CreateSqlContainer("SELECT Id, Geom FROM t");
        var reader = await sc.ExecuteReaderAsync();
        var rows = await DataReaderMapper.LoadAsync<RetentionRow>(reader, MapperOptions.Default);
        Assert.NotNull(Assert.Single(rows).Geom);
        await reader.DisposeAsync();
        return new WeakReference(reader);
    }

    [Fact]
    public async Task MappedReader_IsNotKeptAliveByThePlanCache()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=y;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        }, factory);

        var weak = await MapOnceAsync(context, exec);

        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);
    }
}
