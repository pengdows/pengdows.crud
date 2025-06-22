// using Microsoft.Data.SqlClient;
// using Microsoft.Data.Sqlite;
// using Microsoft.Extensions.DependencyInjection;
// using MySql.Data.MySqlClient;
// using Npgsql;
// using Oracle.ManagedDataAccess.Client;
// using pengdows.crud;
//
// namespace testbed;
//
// public class LocalDockerDbProvider : IDatabaseContextProvider
// {
//     private readonly IServiceProvider _services;
//     private readonly ITypeMapRegistry _typeMap;
//
//     public LocalDockerDbProvider(IServiceProvider services)
//     {
//         _services = services;
//         _typeMap = services.GetRequiredService<ITypeMapRegistry>();
//     }
//
//     public IDatabaseContext Get(string key) => key switch
//     {
//         "PostgreSQL" => new DatabaseContext(
//             "Host=localhost;Port=5432;Username=postgres;Password=mysecretpassword;Database=postgres;",
//             NpgsqlFactory.Instance,
//             _typeMap),
//
//         "Oracle" => new DatabaseContext(
//             "User Id=system;Password=mysecurepassword;Data Source=localhost:51521/XEPDB1;",
//             OracleClientFactory.Instance,
//             _typeMap),
//
//         "SQLServer" => new DatabaseContext(
//             "Server=localhost,1433;Database=testdb;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;",
//             SqlClientFactory.Instance,
//             _typeMap),
//
//         "MySQL" => new DatabaseContext(
//             "Server=localhost;Port=3306;Database=testdb;User=root;Password=rootpassword;",
//             MySqlClientFactory.Instance,
//             _typeMap),
//
//         "SQLite" => new DatabaseContext(
//             "Data Source=mydb.sqlite",
//             SqliteFactory.Instance,
//             _typeMap),
//
//         _ => throw new KeyNotFoundException($"Unknown context key '{key}'")
//     };
// }

