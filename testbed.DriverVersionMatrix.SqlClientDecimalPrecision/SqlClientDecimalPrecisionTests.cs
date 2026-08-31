using System.Data;
using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Xunit;

namespace testbed.DriverVersionMatrix.SqlClientDecimalPrecision;

// FEAT-008: see this project's csproj header comment for the full investigation summary. Locks
// down, against a live SQL Server, that setting DbType.Decimal on a DbParameter and then its
// Value (the exact sequence SqlDialect.CreateDbParameter uses before its own explicit
// Precision = Math.Max(inferred, 18) override) makes SqlClient auto-infer a correct, non-zero
// Precision/Scale from the assigned decimal — the comment's claimed "Precision stays 0, treated
// as DECIMAL(1,0), rejects the value" failure mode never reproduces via this path.
public sealed class SqlClientDecimalPrecisionTests : IAsyncLifetime
{
    private const string Password = "Str0ngP@ssw0rd!";
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU25-GDR2-ubuntu-22.04") // same tag as testbed/SqlServer/SqlServerTestContainer.cs
        .WithEnvironment("ACCEPT_EULA", "Y")
        .WithEnvironment("MSSQL_SA_PASSWORD", Password)
        .WithPortBinding(1433, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(1433);
        _connectionString =
            $"Server=localhost,{hostPort};User Id=sa;Password={Password};TrustServerCertificate=True;Database=master";

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var probe = new SqlConnection(_connectionString);
                await probe.OpenAsync();
                return;
            }
            catch when (attempt < 30)
            {
                await Task.Delay(1000);
            }
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public static IEnumerable<object[]> RepresentativeDecimalValues()
    {
        yield return new object[] { 10m };
        yield return new object[] { 19.99m };
        yield return new object[] { 100m };
        yield return new object[] { 123456.78m };
        yield return new object[] { 0m };
        yield return new object[] { -42.5m };
    }

    [Theory]
    [MemberData(nameof(RepresentativeDecimalValues))]
    public async Task SettingDbTypeThenValue_InfersNonZeroPrecision_AndRoundTripsCorrectly(decimal value)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await using (var create = conn.CreateCommand())
        {
            create.CommandText =
                "IF OBJECT_ID('decimal_precision_probe') IS NOT NULL DROP TABLE decimal_precision_probe; " +
                "CREATE TABLE decimal_precision_probe (id INT PRIMARY KEY, v DECIMAL(18,2))";
            await create.ExecuteNonQueryAsync();
        }

        await using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO decimal_precision_probe (id, v) VALUES (1, @v)";

        // Exact sequence SqlDialect.CreateDbParameter uses before its own defensive override:
        // create the parameter, set the neutral DbType, then Value -- no Precision/Scale touched.
        DbParameter p = insert.CreateParameter();
        p.ParameterName = "@v";
        p.DbType = DbType.Decimal;
        p.Value = value;
        insert.Parameters.Add(p);

        var sqlParam = (SqlParameter)p;
        Assert.True(sqlParam.Precision > 0,
            $"Expected SqlClient to auto-infer a non-zero Precision for {value}, but it stayed at {sqlParam.Precision}.");

        await insert.ExecuteNonQueryAsync(); // must not throw

        await using var select = conn.CreateCommand();
        select.CommandText = "SELECT v FROM decimal_precision_probe WHERE id = 1";
        var roundTripped = (decimal)(await select.ExecuteScalarAsync())!;
        Assert.Equal(value, roundTripped);
    }
}
