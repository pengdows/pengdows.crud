// See https://aka.ms/new-console-template for more information


#region

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using pengdows.crud;
using testbed;

#endregion

// Enable Npgsql legacy timestamp behaviour so that "timestamp without time zone" columns
// can be read as DateTime. Npgsql 6+ made this strict by default; the switch restores the pre-v6 behaviour.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

foreach (var (assembly, type, factory) in DbProviderFactoryFinder.FindAllFactories())
{
    Console.WriteLine($"Found: {type} in {assembly}");
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();

var host = builder.Build();

// Run StormGate integration tests first
await StormGateIntegrationTests.RunAsync();

Console.WriteLine($"Starting parallel database testing at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine();

// Use the new parallel orchestrator (optional providers can be enabled via INCLUDE_* env vars)
var includeOracle = Environment.GetEnvironmentVariable("INCLUDE_ORACLE")?.ToLower() == "true";
var includeSnowflake = Environment.GetEnvironmentVariable("INCLUDE_SNOWFLAKE")?.ToLower() == "true";
var orchestrator = new ParallelTestOrchestrator(host.Services, includeOracle, includeSnowflake);

// Optional filtering: --only A,B or --exclude X,Y or env TESTBED_ONLY/TESTBED_EXCLUDE
static ISet<string> ParseList(string? csv)
{
    return string.IsNullOrWhiteSpace(csv)
        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

string? GetArg(string name)
{
    // poor-man's arg parsing: --name value or --name=value
    foreach (var a in args)
    {
        if (a.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase))
        {
            return a[(name.Length + 3)..];
        }
    }

    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], $"--{name}", StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

var only = ParseList(GetArg("only") ?? Environment.GetEnvironmentVariable("TESTBED_ONLY"));
var exclude = ParseList(GetArg("exclude") ?? Environment.GetEnvironmentVariable("TESTBED_EXCLUDE"));

if (only.Count > 0)
{
    Console.WriteLine($"Filter: only => {string.Join(",", only)}");
}

if (exclude.Count > 0)
{
    Console.WriteLine($"Filter: exclude => {string.Join(",", exclude)}");
}

var results = await orchestrator.RunAllTestsAsync(only, exclude);

// Optional: Export results for CI/CD
var successCount = results.Count(r => r.Success);
var totalCount = results.Count;
var totalChecks = results.Sum(r => r.ChecksPassed);
var totalSkipped = results.Sum(r => r.ChecksSkipped);

if (successCount == totalCount)
{
    Console.WriteLine($"🎉 All {totalCount} databases passed ({totalChecks} checks, {totalSkipped} skipped)!");
    Environment.Exit(0);
}
else
{
    Console.WriteLine($"❌ {totalCount - successCount}/{totalCount} databases failed ({totalChecks} checks passed, {totalSkipped} skipped)");
    Environment.Exit(1);
}
