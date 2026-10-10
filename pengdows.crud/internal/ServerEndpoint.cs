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
            if (TrySplitBracketedHost(raw, out var bracketedHost, out var bracketedPort))
            {
                port = bracketedPort;
                raw = bracketedHost;
            }
            else if (comma > 0 && int.TryParse(raw[(comma + 1)..].Trim(), out var commaPort))
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

    // "[2001:db8::1]:5432" and "[2001:db8::1],1433": the colons inside the brackets belong to the
    // address, so the port is whatever follows the closing bracket.
    private static bool TrySplitBracketedHost(string value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (!value.StartsWith('['))
        {
            return false;
        }

        var close = value.IndexOf(']');
        if (close < 0)
        {
            return false;
        }

        var rest = value[(close + 1)..].Trim();
        if (rest.Length < 2 || (rest[0] != ':' && rest[0] != ','))
        {
            return false;
        }

        if (!int.TryParse(rest[1..].Trim(), out port))
        {
            return false;
        }

        host = value[..(close + 1)];
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
