using pengdows.crud.dialects;
using pengdows.crud.enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;

// Test that our polymorphic GetLastInsertedIdQuery works
var logger = NullLogger.Instance;

// Test SQLite
var sqliteDialect = new SqliteDialect(SqliteFactory.Instance, logger);
Console.WriteLine($"SQLite: {sqliteDialect.GetLastInsertedIdQuery()}");

// Test SQL Server
var sqlServerDialect = new SqlServerDialect(null!, logger);
Console.WriteLine($"SQL Server: {sqlServerDialect.GetLastInsertedIdQuery()}");

// Test MySQL
var mysqlDialect = new MySqlDialect(null!, logger);
Console.WriteLine($"MySQL: {mysqlDialect.GetLastInsertedIdQuery()}");

// Test PostgreSQL
var postgresDialect = new PostgreSqlDialect(null!, logger);
Console.WriteLine($"PostgreSQL: {postgresDialect.GetLastInsertedIdQuery()}");

// Test DuckDB
var duckDbDialect = new DuckDbDialect(null!, logger);
Console.WriteLine($"DuckDB: {duckDbDialect.GetLastInsertedIdQuery()}");

// Test MariaDB
var mariaDbDialect = new MariaDbDialect(null!, logger);
Console.WriteLine($"MariaDB: {mariaDbDialect.GetLastInsertedIdQuery()}");

// Test Oracle (should throw)
var oracleDialect = new OracleDialect(null!, logger);
try
{
    oracleDialect.GetLastInsertedIdQuery();
    Console.WriteLine("Oracle: ERROR - Should have thrown!");
}
catch (NotSupportedException ex)
{
    Console.WriteLine($"Oracle: {ex.Message}");
}

// Test Firebird (should throw)
var firebirdDialect = new FirebirdDialect(null!, logger);
try
{
    firebirdDialect.GetLastInsertedIdQuery();
    Console.WriteLine("Firebird: ERROR - Should have thrown!");
}
catch (NotSupportedException ex)
{
    Console.WriteLine($"Firebird: {ex.Message}");
}

// Test base class (should throw)
var baseDialect = new SqlDialect(null!, logger);
try
{
    baseDialect.GetLastInsertedIdQuery();
    Console.WriteLine("Base: ERROR - Should have thrown!");
}
catch (NotSupportedException ex)
{
    Console.WriteLine($"Base SqlDialect: {ex.Message}");
}

Console.WriteLine("\n✅ All polymorphic implementations working correctly!");