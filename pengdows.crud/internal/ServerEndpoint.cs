using System.Data.Common;

namespace pengdows.crud.@internal;

/// <summary>Decides whether two connection strings reach the same server.</summary>
/// <remarks>
/// The key is host[\instance]:port. The database name, credentials, application name and pool
/// settings are not part of it: a server's connection limit is server-wide, and the reader and
/// writer variants of one context already differ in application name and pool settings.
/// A read replica has a different host, so it never shares a budget with its primary.
/// Loopback spellings (localhost, 127.0.0.1, ::1, ., (local)) normalize to "localhost", and an omitted
/// port equals the dialect's default port when one is supplied.
/// "Data Source" may be a file for embedded engines; use this only for dialects that talk to a server.
/// </remarks>
internal static class ServerEndpoint
{
    private static readonly string[] HostKeys =
        { "host", "server", "data source", "datasource", "address", "addr", "network address" };

    private static readonly HashSet<string> Loopback = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "127.0.0.1", "::1", "[::1]", ".", "(local)"
    };

    public static bool TryGetKey(DbConnectionStringBuilder builder, int? defaultPort, out string key)
    {
        key = string.Empty;

        string? raw = null;
        string? hostKeyName = null;
        foreach (var name in HostKeys)
        {
            if (builder.TryGetValue(name, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
            {
                raw = text.Trim();
                hostKeyName = name;
                break;
            }
        }

        if (raw is null)
        {
            return false;
        }

        int? port = null;

        // "Host" may legitimately be a comma-separated list of hosts (multi-host failover), so a
        // comma there is not a port separator. Elsewhere "host,port" is the SQL Server form.
        if (!string.Equals(hostKeyName, "host", StringComparison.Ordinal))
        {
            raw = StripProtocolPrefix(raw);

            var comma = raw.LastIndexOf(',');
            if (comma > 0 && int.TryParse(raw[(comma + 1)..].Trim(), out var commaPort))
            {
                port = commaPort;
                raw = raw[..comma].Trim();
            }
            else if (raw.Count(c => c == ':') == 1)
            {
                var colon = raw.IndexOf(':');
                if (int.TryParse(raw[(colon + 1)..].Trim(), out var colonPort))
                {
                    port = colonPort;
                    raw = raw[..colon].Trim();
                }
            }
        }

        string? instance = null;
        var slash = raw.IndexOf('\\');
        if (slash > 0)
        {
            instance = raw[(slash + 1)..].Trim().ToLowerInvariant();
            raw = raw[..slash].Trim();
        }

        var host = Loopback.Contains(raw) ? "localhost" : raw.ToLowerInvariant();
        if (host.Length == 0)
        {
            return false;
        }

        if (builder.TryGetValue("port", out var portValue) && int.TryParse(portValue?.ToString(), out var explicitPort))
        {
            port = explicitPort;
        }

        port ??= defaultPort;

        key = $"{host}{(instance is null ? string.Empty : "\\" + instance)}:{port?.ToString() ?? string.Empty}";
        return true;
    }

    private static string StripProtocolPrefix(string value)
    {
        foreach (var prefix in new[] { "tcp:", "np:", "lpc:" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..].Trim();
            }
        }

        return value;
    }
}
