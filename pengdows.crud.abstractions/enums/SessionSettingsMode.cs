namespace pengdows.crud.enums;

/// <summary>
/// Controls which session settings are sent when a pooled connection is checked out.
/// </summary>
/// <remarks>
/// <see cref="Normal"/> sends a managed setting only when the server's value at pool creation differs from what
/// pengdows requires. <see cref="Paranoid"/> sends every managed setting that applies on every checkout, whatever the
/// server reported, so a connection a caller poisoned is put back on its next checkout. Paranoid is for diagnosing odd
/// session-state problems and costs one batch of settings per checkout; it is not needed for normal use.
/// </remarks>
public enum SessionSettingsMode
{
    /// <summary>Default. Send only the settings whose captured server value differs from the requirement.</summary>
    Normal = 0,

    /// <summary>Send every managed setting that applies on every checkout.</summary>
    Paranoid = 1,
}
