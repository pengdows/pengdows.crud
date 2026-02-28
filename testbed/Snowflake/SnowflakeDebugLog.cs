using System.Diagnostics;

namespace testbed.Snowflake;

internal static class SnowflakeDebugLog
{
    private static readonly object Gate = new();
    private const string LogPath = "/tmp/snowflake-debug.log";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("SNOWFLAKE_DEBUG"), "true",
            StringComparison.OrdinalIgnoreCase);

    public static void Log(string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        var line = $"[{DateTime.UtcNow:O}] {message}";
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore logging failures.
        }

        Debug.WriteLine(line);
        Console.WriteLine(line);
    }
}
