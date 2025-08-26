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

await using var liteDb = new DatabaseContext("Data Source=mydb.sqlite", SqliteFactory.Instance, null);
var lite = new TestProvider(liteDb, host.Services);
await lite.RunTest();
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

await using var my = new MySqlTestContainer();
await my.RunTestWithContainerAsync<TestProvider>(host.Services, (db, sp) => new TestProvider(db, sp));
await using var maria = new MariaDbContainer();
await maria.RunTestWithContainerAsync<TestProvider>(host.Services, (db, sp) => new TestProvider(db, sp));
await using var pg = new PostgreSqlTestContainer();
await pg.RunTestWithContainerAsync<PostgreSQLTestProvider>(host.Services,
    (db, sp) => new PostgreSQLTestProvider(db, sp));
await using var ms = new SqlServerTestContainer();
await ms.RunTestWithContainerAsync<TestProvider>(host.Services, (db, sp) => new TestProvider(db, sp));
// var o = new OracleTestContainer();
// await o.RunTestWithContainerAsync<OracleTestProvider>(host.Services, (db, sp) => new OracleTestProvider(db, sp));
// var oracleConnectionString = "User Id=system;Password=mysecurepassword; Data Source=localhost:51521/XEPDB1;";
// var oracleDb = new DatabaseContext(oracleConnectionString, OracleClientFactory.Instance,
//     null);
// var oracle = new OracleTestProvider(oracleDb, host.Services);
// await oracle.RunTest();


await using var fb = new FirebirdSqlTestContainer();
await fb.RunTestWithContainerAsync(host.Services, (db, sp) => new FirebirdTestProvider(db, sp));


Console.WriteLine("All tests complete.");