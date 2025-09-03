// See https://aka.ms/new-console-template for more information


#region

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using pengdows.crud;
using testbed;

#endregion

foreach (var (assembly, type, factory) in DbProviderFactoryFinder.FindAllFactories())
{
    Console.WriteLine($"Found: {type} in {assembly}");
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();

var host = builder.Build();

Console.WriteLine($"Starting parallel database testing at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine();

// Use the new parallel orchestrator (Oracle can be enabled via INCLUDE_ORACLE=true)
var includeOracle = Environment.GetEnvironmentVariable("INCLUDE_ORACLE")?.ToLower() == "true";
var orchestrator = new ParallelTestOrchestrator(host.Services, includeOracle);

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

if (successCount == totalCount)
{
    Console.WriteLine("🎉 All tests passed!");
    Environment.Exit(0);
}
else
{
    Console.WriteLine($"❌ {totalCount - successCount} out of {totalCount} tests failed");
    Environment.Exit(1);
}
