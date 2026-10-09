namespace pengdows.crud.@internal;

internal enum ConnectionCeilingLimit
{
    Requested,
    ProviderDefault,
    ServerConfigured,
    DialectAbsolute
}

internal readonly record struct ConnectionCeilingResult(
    int Value,
    ConnectionCeilingLimit Limiter,
    bool HeadroomApplied = false)
{
    /// <summary>True when the server or the dialect, not the caller or the default, set the value.</summary>
    public bool WasClamped => Limiter is ConnectionCeilingLimit.ServerConfigured or ConnectionCeilingLimit.DialectAbsolute;
}

/// <summary>The one rule for how many connections a context may use against a server.</summary>
/// <remarks>
/// The effective ceiling is the smallest of what the caller requested (or, when nothing was requested,
/// the provider default) and what the server reports it is configured to allow.
/// The provider default applies only when nothing was requested: an explicit request is never capped
/// by it, so a caller who asks for 200 against a server that allows 500 gets 200.
/// A non-positive server value means "unknown or unlimited" (SQL Server reports 0 for an unlimited
/// "user connections") and is ignored.
/// Headroom is the slots the application asks to leave free on the server for other clients. It reserves
/// server capacity, so it comes off the server's usable limit when that is known, not off a request that
/// already fits. Headroom that consumes the whole server limit is a configuration error and is rejected
/// rather than silently becoming 1. With no known server limit there is nothing to reserve from, and the
/// result says so (HeadroomApplied = false).
/// Pure and side-effect free: probing the server and applying the result live elsewhere.
/// </remarks>
internal static class ConnectionCeiling
{
    /// <param name="requested">The caller's pool size; null (or non-positive) when nothing was requested.</param>
    /// <param name="serverConfigured">The server's own configured connection limit, when it could be read.</param>
    /// <param name="dialectAbsolute">The most this kind of server can ever allow.</param>
    /// <param name="providerDefault">The provider's default pool size, used only when nothing was requested.</param>
    /// <param name="headroom">Server slots to leave free for other clients; 0 reserves nothing.</param>
    public static ConnectionCeilingResult Resolve(
        int? requested,
        int? serverConfigured,
        int? dialectAbsolute,
        int providerDefault,
        int headroom = 0)
    {
        if (headroom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(headroom), headroom, "Headroom must be >= 0.");
        }

        var headroomApplied = false;
        int? serverUsable = serverConfigured is > 0 ? serverConfigured : null;
        if (serverUsable.HasValue && headroom > 0)
        {
            if (headroom >= serverUsable.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(headroom), headroom,
                    $"Headroom of {headroom} leaves nothing of the server's {serverUsable.Value}-connection limit; " +
                    "lower the headroom or raise the server's limit.");
            }

            serverUsable -= headroom;
            headroomApplied = true;
        }

        var hasRequest = requested is > 0;
        var value = hasRequest ? requested!.Value : providerDefault;
        var limiter = hasRequest ? ConnectionCeilingLimit.Requested : ConnectionCeilingLimit.ProviderDefault;

        if (serverUsable.HasValue && serverUsable.Value < value)
        {
            value = serverUsable.Value;
            limiter = ConnectionCeilingLimit.ServerConfigured;
        }

        if (dialectAbsolute is > 0 && dialectAbsolute.Value < value)
        {
            value = dialectAbsolute.Value;
            limiter = ConnectionCeilingLimit.DialectAbsolute;
        }

        return new ConnectionCeilingResult(value, limiter, headroomApplied);
    }
}
