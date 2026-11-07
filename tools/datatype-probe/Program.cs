// Live datatype probe: for one database, starts its testbed container, then asks the server
// whether it accepts each candidate column declaration for each semantic type, and reads back
// the provider's own type name and CLR type for the accepted ones. Results go to a JSON-lines file.
//
// Usage: dotnet run -- <DatabaseName> <results.jsonl>
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using testbed;
using testbed.Firebird;
using testbed.mariaDb;
using testbed.SqlServer;
using testbed.Oracle;
using testbed.PostgreSQL;
using testbed.Cockroach;
using testbed.MySQL;
using testbed.DuckDb;
using testbed.Yugabyte;
using testbed.TiDB;
using testbed.SingleStore;
using testbed.Sybase;
using testbed.Spanner;
using testbed.FlatFile;
using testbed.Access;
using testbed.Db2;
using testbed.Informix;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var dbName = args[0];
var outPath = args[1];
// Optional: the container image to run (a testbed version-matrix entry). Default: the testbed's latest image.
var image = args.Length > 2 ? args[2] : null;

ITestContainer Make(string name) => name switch
{
    "PostgreSql" => new PostgreSqlTestContainer(image),
    "SqlServer" => new SqlServerTestContainer(image),
    "Oracle" => new OracleTestContainer(image),
    "Firebird" => new FirebirdSqlTestContainer(image),
    "CockroachDb" => new CockroachDbTestContainer(image),
    "MariaDb" => new MariaDbContainer(image),
    "MySql" => new MySqlTestContainer(image),
    "Sqlite" => new SqliteTestContainer(),
    "DuckDB" => new DuckDbTestContainer(),
    "YugabyteDb" => new YugabyteTestContainer(image),
    "TiDb" => new TiDBTestContainer(image),
    "SingleStore" => new SingleStoreTestContainer(),
    "SybaseASE" => new SybaseTestContainer(),
    "Spanner" => new SpannerOmniTestContainer(),
    "FlatFile" => new FlatFileTestContainer(),
    "Access" => new AccessTestContainer(),
    "Db2" => new Db2TestContainer(image),
    "Informix" => new InformixTestContainer(image),
    _ => throw new ArgumentException($"no container for {name}")
};

var candidates = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "candidates.json")))!;

static string Short(Exception ex)
{
    var msg = ex.Message.ReplaceLineEndings(" ");
    return msg.Length > 220 ? msg[..220] : msg;
}

using var outFile = new StreamWriter(outPath, append: false);
void Emit(object record) => outFile.WriteLine(JsonSerializer.Serialize(record));

var container = Make(dbName);
try
{
    await container.StartAsync();
}
catch (Exception ex)
{
    Emit(new { db = dbName, containerError = Short(ex) });
    return;
}

var services = new ServiceCollection().BuildServiceProvider();
var ctx = await container.GetDatabaseContextAsync(services);

async Task<string?> TryExecAsync(string sql)
{
    try
    {
        await using var sc = ctx.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
        return null;
    }
    catch (Exception ex)
    {
        return Short(ex);
    }
}

var n = 0;
foreach (var (semanticType, decls) in candidates)
{
    if (decls.Length == 0)
    {
        Emit(new { db = dbName, type = semanticType, decl = (string?)null, accepted = false, notProbed = true });
        continue;
    }

    foreach (var decl in decls)
    {
        n++;
        var table = $"pr{n}";
        await TryExecAsync($"DROP TABLE {table}");

        var createError = await TryExecAsync($"CREATE TABLE {table} (id INTEGER NOT NULL PRIMARY KEY, c {decl})");
        if (createError != null)
        {
            Emit(new { db = dbName, type = semanticType, decl, accepted = false, stage = "create", error = createError });
            continue;
        }

        string? providerType = null, clrType = null, selectError = null;
        try
        {
            await using var sc = ctx.CreateSqlContainer($"SELECT c FROM {table}");
            using var reader = await sc.ExecuteReaderAsync();
            providerType = reader.GetDataTypeName(0);
            clrType = reader.GetFieldType(0)?.FullName;
        }
        catch (Exception ex)
        {
            selectError = Short(ex);
        }

        await TryExecAsync($"DROP TABLE {table}");

        if (selectError != null)
        {
            Emit(new { db = dbName, type = semanticType, decl, accepted = false, stage = "select", error = selectError });
        }
        else
        {
            Emit(new { db = dbName, type = semanticType, decl, accepted = true, providerType, clrType });
        }
    }
}

await container.DisposeAsync();
Console.WriteLine($"{dbName}: probed {n} declarations -> {outPath}");
