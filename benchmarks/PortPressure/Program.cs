// Port-pressure probe: runs one storm of 1,000 concurrent operations against PostgreSQL and prints the outcome.
// Arms:
//   uncontrolled      Dapper, pooling off: every operation opens its own TCP connection.
//   governed-default  Pengdows, Standard mode, default pool (up to 100 connections, more than the server allows).
//   governed-capped   Pengdows, Standard mode, pool capped at the server's connection limit (Maximum Pool Size=20).
//   governed-clamped  Pengdows, Standard mode, default pool with ClampPoolsToServerConnectionLimit (the library reads the server limit).
// Usage: PortPressure <arm> <connection-string-without-pool-settings>
using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Npgsql;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.metrics;

const int Parallelism = 1000;
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PortPressure <uncontrolled|governed-default|governed-capped|governed-clamped> <connection-string>");
    return 2;
}

var arm = args[0];
var baseConn = args[1];
var errors = new ConcurrentDictionary<string, int>();
var ok = 0;
DatabaseMetrics? metrics = null;

if (arm == "uncontrolled")
{
    var connStr = baseConn + ";Pooling=false";
    await Task.WhenAll(Enumerable.Range(0, Parallelism).Select(i => Task.Run(async () =>
    {
        try
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            await conn.QueryFirstOrDefaultAsync<(int id, int value)>(
                "SELECT id, value FROM port_pressure WHERE id = @id", new { id = i % 100 + 1 });
            Interlocked.Increment(ref ok);
        }
        catch (Exception ex)
        {
            errors.AddOrUpdate(ex.GetType().Name, 1, (_, n) => n + 1);
        }
    })));
}
else if (arm == "governed-default" || arm == "governed-capped" || arm == "governed-clamped")
{
    var connStr = arm == "governed-capped" ? baseConn + ";Maximum Pool Size=20" : baseConn;
    var clamp = arm == "governed-clamped";
    using var ctx = new DatabaseContext(new DatabaseContextConfiguration
    {
        ConnectionString = connStr,
        DbMode = DbMode.Standard,
        ReadWriteMode = ReadWriteMode.ReadWrite,
        PoolAcquireTimeout = TimeSpan.FromSeconds(30),
        ClampPoolsToServerConnectionLimit = clamp,
        EnableMetrics = true
    }, NpgsqlFactory.Instance);
    await Task.WhenAll(Enumerable.Range(0, Parallelism).Select(i => Task.Run(async () =>
    {
        try
        {
            await using var sc = ctx.CreateSqlContainer();
            sc.Query.Append("SELECT id, value FROM port_pressure WHERE id = ");
            sc.Query.Append(sc.MakeParameterName("id"));
            sc.AddParameterWithValue("id", DbType.Int32, i % 100 + 1);
            await using var reader = await sc.ExecuteReaderAsync(ExecutionType.Read);
            await reader.ReadAsync();
            Interlocked.Increment(ref ok);
        }
        catch (Exception ex)
        {
            errors.AddOrUpdate(ex.GetType().Name, 1, (_, n) => n + 1);
        }
    })));
    metrics = ctx.Metrics;
}
else
{
    Console.Error.WriteLine($"unknown arm: {arm}");
    return 2;
}

var detail = string.Join(", ", errors.Select(pair => $"{pair.Key} x{pair.Value}"));
Console.WriteLine($"{arm}: ok={ok} failed={Parallelism - ok}" + (detail.Length == 0 ? "" : " " + detail));
if (metrics is not null)
{
    Console.WriteLine($"{arm}: metrics readRequests={metrics.ReadRequests} readPoolWaits={metrics.ReadPoolWaits} " +
                      $"readPoolTimeouts={metrics.ReadPoolTimeouts} connectionsOpened={metrics.ConnectionsOpened} " +
                      $"peakOpenConnections={metrics.PeakOpenConnections}");
}
return 0;
