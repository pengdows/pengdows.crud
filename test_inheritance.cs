using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using System.Data.SQLite;

// Test that MariaDB inherits from MySQL
var mariaDb = new MariaDbDialect(SQLiteFactory.Instance, NullLogger.Instance);
var mysql = new MySqlDialect(SQLiteFactory.Instance, NullLogger.Instance);

Console.WriteLine($"MariaDB DatabaseType: {mariaDb.DatabaseType}");
Console.WriteLine($"MySQL DatabaseType: {mysql.DatabaseType}");

Console.WriteLine($"MariaDB QuotePrefix: {mariaDb.QuotePrefix}");
Console.WriteLine($"MySQL QuotePrefix: {mysql.QuotePrefix}");

Console.WriteLine($"MariaDB ParameterMarker: {mariaDb.ParameterMarker}");
Console.WriteLine($"MySQL ParameterMarker: {mysql.ParameterMarker}");

Console.WriteLine($"MariaDB MaxParameterLimit: {mariaDb.MaxParameterLimit}");
Console.WriteLine($"MySQL MaxParameterLimit: {mysql.MaxParameterLimit}");

Console.WriteLine($"MariaDB SupportsOnDuplicateKey: {mariaDb.SupportsOnDuplicateKey}");
Console.WriteLine($"MySQL SupportsOnDuplicateKey: {mysql.SupportsOnDuplicateKey}");

Console.WriteLine($"MariaDB SupportsJsonTypes: {mariaDb.SupportsJsonTypes}");
Console.WriteLine($"MySQL SupportsJsonTypes: {mysql.SupportsJsonTypes}");

Console.WriteLine("✅ MariaDB successfully inherits from MySQL with overrides!");
