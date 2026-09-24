using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace pengdows.crud.@internal;

// Process-wide cache of normalized connection-string maps.
//
// The key is a SHA-256 digest, never the raw connection string: most providers embed credentials
// in the connection string (Password=..., Pwd=..., User Id=...), and a raw-string key kept every
// credential ever seen — including rotated-out or per-tenant ones — for the process lifetime. The
// cache is bounded so per-tenant or rotated credentials can't grow it without limit.
//
// The digest covers every input that shapes the cached map (the read-only key/value, application
// name setting and read-only suffix), not just the connection string, so the same connection
// string normalized with different parameters never gets another caller's map back.
internal static class ConnectionStringNormalizationCache
{
    // Distinct connection strings per process are typically few (one per tenant/provider
    // combination); 256 covers realistic multi-tenant fleets while bounding worst-case growth.
    private const int MaxEntries = 256;

    private static readonly BoundedCache<string, Dictionary<string, string>> Cache = new(MaxEntries);

    internal static bool TryGet(string connectionString, out Dictionary<string, string>? normalized)
    {
        return TryGet(connectionString, null, null, null, string.Empty, out normalized);
    }

    internal static bool TryGet(
        string connectionString,
        string? readOnlyKey,
        string? readOnlyValue,
        string? applicationNameSettingName,
        string readOnlySuffix,
        out Dictionary<string, string>? normalized)
    {
        return Cache.TryGet(
            HashKey(connectionString, readOnlyKey, readOnlyValue, applicationNameSettingName, readOnlySuffix),
            out normalized);
    }

    internal static bool TryAdd(string connectionString, Dictionary<string, string> normalized)
    {
        return TryAdd(connectionString, null, null, null, string.Empty, normalized);
    }

    internal static bool TryAdd(
        string connectionString,
        string? readOnlyKey,
        string? readOnlyValue,
        string? applicationNameSettingName,
        string readOnlySuffix,
        Dictionary<string, string> normalized)
    {
        var wasAdded = false;
        Cache.GetOrAdd(
            HashKey(connectionString, readOnlyKey, readOnlyValue, applicationNameSettingName, readOnlySuffix),
            _ =>
            {
                wasAdded = true;
                return normalized;
            });

        return wasAdded;
    }

    internal static void ClearForTests()
    {
        Cache.Clear();
    }

    internal static int Count => Cache.Count;

    private static string HashKey(
        string connectionString,
        string? readOnlyKey,
        string? readOnlyValue,
        string? applicationNameSettingName,
        string readOnlySuffix)
    {
        // \u0000 separates the parts; \u0001 marks a null so null and "" hash differently.
        var material = string.Join('\u0000',
            connectionString,
            readOnlyKey ?? "\u0001",
            readOnlyValue ?? "\u0001",
            applicationNameSettingName ?? "\u0001",
            readOnlySuffix);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
