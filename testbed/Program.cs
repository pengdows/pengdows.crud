// See https://aka.ms/new-console-template for more information


#region

using System.Data.Common;
using AdoNetCore.AseClient;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Oracle.ManagedDataAccess.Client;
using pengdows.crud;
using testbed;
using testbed.Cockroach;

#endregion

foreach (var (assembly, type, factory) in DbProviderFactoryFinder.FindAllFactories())
{
    Console.WriteLine($"Found: {type} in {assembly}");
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();
 
var host = builder.Build();

await using (var liteDb = new DatabaseContext("Data Source=mydb.sqlite", SqliteFactory.Instance,
                 null))
{
    var lite = new TestProvider(liteDb, host.Services);
    await lite.RunTest();
    liteDb.Dispose();
}
await using var duck = new DuckDbTestContainer();
await duck.RunTestWithContainerAsync<TestProvider>(host.Services, (db, sp) => new TestProvider(db, sp));
// var cs = $"DataSource=localhost;Port=5000;Database={0};Uid=sa;Pwd=MyStr0ngP@ssw0rd;";
//
// // wait for ASE to be ready
// //await using (var mc = new DatabaseContext(string.Format(cs, "master"), AseClientFactory.Instance))
// {
//     // for(var time = DateT;DateTime.UtcNow-time >
//
//     await using var conn = AseClientFactory.Instance.CreateConnection();
//     conn.ConnectionString = cs;
//
//     await conn.OpenAsync();
//
//     await using var cmd = conn.CreateCommand();
//     cmd.CommandText = $"IF NOT EXISTS (SELECT name FROM sysdatabases WHERE name = 'testdb') " +
//                       $"CREATE DATABASE testdb";
//     await cmd.ExecuteNonQueryAsync();
// }
//
// ;

// await using (var sybase = new DatabaseContext(string.Format(cs, "testdb"), AseClientFactory.Instance))
// {
//     var sy = new TestProvider(sybase, host.Services);
//     await sy.RunTest();
// }
// await using var sybase = new SybaseTestContainer();
// await sybase.RunTestWithContainerAsync(host.Services, (db, sp) => new SybaseTestProvider(db, sp));

await using var cockroach = new CockroachDbTestContainer();
await cockroach.RunTestWithContainerAsync(host.Services, (db, sp) => new CockroachDbTestProvider(db, sp));

await using (var my = new MySqlTestContainer())
{
    await my.RunTestWithContainerAsync<TestProvider>(host.Services, (db, sp) => new TestProvider(db, sp));
}

var maria = new MariaDbContainer();
await maria.RunTestWithContainerAsync<TestProvider>(host.Services, (db, sp) => new TestProvider(db, sp));
var pg = new PostgreSqlTestContainer();
await pg.RunTestWithContainerAsync<PostgreSQLTestProvider>(host.Services,
    (db, sp) => new PostgreSQLTestProvider(db, sp));
var ms = new SqlServerTestContainer();
await ms.RunTestWithContainerAsync<TestProvider>(host.Services, (db, sp) => new TestProvider(db, sp));
// var o = new OracleTestContainer();
// await o.RunTestWithContainerAsync<OracleTestProvider>(host.Services, (db, sp) => new OracleTestProvider(db, sp));
// var oracleConnectionString = "User Id=system;Password=mysecurepassword; Data Source=localhost:51521/XEPDB1;";
// var oracleDb = new DatabaseContext(oracleConnectionString, OracleClientFactory.Instance,
//     null);
// var oracle = new OracleTestProvider(oracleDb, host.Services);
// await oracle.RunTest();


var fb = new FirebirdSqlTestContainer();
await fb.RunTestWithContainerAsync(host.Services, (db, sp) => new FirebirdTestProvider(db, sp));


Console.WriteLine("All tests complete.");

async Task WaitForDbToStart(DbProviderFactory instance, string connectionString,
    int numberOfSecondsToWait = 60)
{
    var csb = instance.CreateConnectionStringBuilder();
    csb.ConnectionString = connectionString;

    var timeout = TimeSpan.FromSeconds(numberOfSecondsToWait);
    var startTime = DateTime.UtcNow;

    var lastError = String.Empty;
    while (DateTime.UtcNow - startTime < timeout)
    {
        await using var connection = instance.CreateConnection();
        try
        {
            connection.ConnectionString = csb.ConnectionString;

            await connection.OpenAsync();
            await connection.CloseAsync();
            return;
        }
        catch (OracleException ex)
        {
            Console.WriteLine(ex);
            await Task.Delay(1000);
        }
        catch (FbException ex) when (ex.Message.Contains("I/O error"))
        {
            try
            {
                if (csb is not FbConnectionStringBuilder orig)
                {
                    throw new InvalidOperationException("Connection string builder is not valid.");
                }

                var db = orig.Database;
                if (string.IsNullOrWhiteSpace(db))
                {
                    throw new InvalidOperationException("Database path is not specified.");
                }

                var csbTemp = new FbConnectionStringBuilder
                {
                    DataSource = orig.DataSource,
                    Port = orig.Port,
                    UserID = orig.UserID,
                    Password = orig.Password,
                    Charset = orig.Charset,
                    Pooling = false
                };

                await using var createConn = instance.CreateConnection();
                createConn.ConnectionString = csbTemp.ConnectionString;

                await using var createCmd = createConn.CreateCommand();
                createCmd.CommandText = $"CREATE DATABASE '{db}';";

                await createConn.OpenAsync();
                await createCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex1)
            {
                Console.WriteLine(ex1);
            }
        }
        catch (AseException aseException)
        {
            Console.WriteLine(aseException);
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            var currentError = ex.Message;
            if (currentError != lastError)
            {
                Console.WriteLine(currentError);
            }

            lastError = currentError;
            await Task.Delay(1000);
        }
    }

    throw new TimeoutException($"Could not connect after {numberOfSecondsToWait}s.");
}