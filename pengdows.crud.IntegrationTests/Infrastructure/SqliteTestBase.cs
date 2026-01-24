using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Lightweight base class for SQLite-only integration tests.
/// No Docker containers required - uses in-memory SQLite.
/// </summary>
public abstract class SqliteTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected readonly IHost Host;
    protected IDatabaseContext Context = null!;
    protected TypeMapRegistry TypeMap = null!;

    protected SqliteTestBase(ITestOutputHelper output)
    {
        Output = output;

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();
        Host = builder.Build();
    }

    public virtual async Task InitializeAsync()
    {
        await Host.StartAsync();

        TypeMap = new TypeMapRegistry();

        // Use in-memory SQLite - SingleConnection mode keeps the database alive
        Context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            TypeMap);

        Output.WriteLine($"SQLite context created with mode: {Context.ConnectionMode}");

        // Run setup
        await SetupDatabaseAsync();
    }

    public virtual async Task DisposeAsync()
    {
        Context.Dispose();
        await Host.StopAsync();
        Host.Dispose();
    }

    /// <summary>
    /// Override to perform database setup (create tables, etc.)
    /// </summary>
    protected virtual Task SetupDatabaseAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get the audit resolver from the host services
    /// </summary>
    protected IAuditValueResolver GetAuditResolver()
    {
        return Host.Services.GetService<IAuditValueResolver>()
               ?? new StringAuditContextProvider();
    }

    /// <summary>
    /// Execute raw SQL against the database
    /// </summary>
    protected async Task ExecuteSqlAsync(string sql)
    {
        await using var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}
