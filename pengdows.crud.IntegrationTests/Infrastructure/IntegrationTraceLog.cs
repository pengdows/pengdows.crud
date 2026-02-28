using System.Runtime.CompilerServices;
using pengdows.crud.enums;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

internal static class IntegrationTraceLog
{
    private static readonly object TraceGate = new();

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
                File.AppendAllText(GetTracePath(provider), line + Environment.NewLine);
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

    private static string GetTracePath(SupportedDatabase provider)
    {
        return provider == SupportedDatabase.Snowflake
            ? "/tmp/snowflake-debug.log"
            : "/tmp/integration-trace.log";
    }
}
