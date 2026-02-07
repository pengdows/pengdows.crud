using System.Collections.Concurrent;
using System.Collections.Generic;

namespace pengdows.crud.@internal;

internal static class ConnectionStringNormalizationCache
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Cache =
        new(StringComparer.Ordinal);

    internal static bool TryGet(string connectionString, out Dictionary<string, string>? normalized)
    {
        return Cache.TryGetValue(connectionString, out normalized);
    }

    internal static bool TryAdd(string connectionString, Dictionary<string, string> normalized)
    {
        return Cache.TryAdd(connectionString, normalized);
    }

    internal static void ClearForTests()
    {
        Cache.Clear();
    }

    internal static int Count => Cache.Count;
}
