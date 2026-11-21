using System.Runtime.CompilerServices;
using pengdows.crud.enums;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

internal static class IntegrationTraceLog
{
    private static readonly object TraceGate = new();
    private static readonly HashSet<SupportedDatabase> Announced = new();

    public static bool IsEnabled(SupportedDatabase provider)
    {
        return IsGeneralTraceEnabled() || IsSnowflakeTraceEnabled(provider);
    }

    public static void Write(
        SupportedDatabase provider,
        string message,
        ITestOutputHelper? output = null,
        [CallerMemberName] string? caller = null)
    {
        if (!IsEnabled(provider))
        {
            return;
        }

        var line = $"[{DateTime.UtcNow:O}] [{provider}] {caller}: {message}";

        try
        {
            output?.WriteLine(line);
        }
        catch
        {
            // Ignore test output failures.
        }

        Console.WriteLine(line);

        try
        {
            lock (TraceGate)
            {
                if (!Announced.Contains(provider))
                {
                    Announced.Add(provider);
                    Console.WriteLine($"[trace] {provider} lines are also appended to {TracePath(provider)}");
                }

                File.AppendAllText(TracePath(provider), line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore trace file failures.
        }
    }

    private static bool IsGeneralTraceEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable("INTEGRATION_TRACE"), "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSnowflakeTraceEnabled(SupportedDatabase provider)
    {
        return provider == SupportedDatabase.Snowflake &&
               string.Equals(Environment.GetEnvironmentVariable("SNOWFLAKE_DEBUG"), "true",
                   StringComparison.OrdinalIgnoreCase);
    }

    // One random suffix per process, so a run appends to one file that no other local account could have created.
    private static readonly string ProcessId = Guid.NewGuid().ToString("N");
    private static readonly string GeneralTracePath =
        Path.Combine(Path.GetTempPath(), "pengdows-integration-trace-" + ProcessId + ".log");
    private static readonly string SnowflakeTracePath =
        Path.Combine(Path.GetTempPath(), "pengdows-snowflake-debug-" + ProcessId + ".log");

    internal static string TracePath(SupportedDatabase provider) =>
        provider == SupportedDatabase.Snowflake ? SnowflakeTracePath : GeneralTracePath;
}
