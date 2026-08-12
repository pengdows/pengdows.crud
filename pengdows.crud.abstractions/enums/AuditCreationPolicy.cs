namespace pengdows.crud.enums;

/// <summary>
/// Controls how audit-field population treats an entity's existing <c>CreatedBy</c>/<c>CreatedOn</c>
/// values when creating a row.
/// </summary>
public enum AuditCreationPolicy
{
    /// <summary>
    /// Default. An existing non-default value on the entity is preserved rather than
    /// overwritten by the audit resolver — useful for imports/migrations that need to
    /// carry an original creation timestamp/author. This is the current (2.0) behavior.
    /// </summary>
    PreserveExplicitValues = 0,

    /// <summary>
    /// Always overwrite <c>CreatedBy</c>/<c>CreatedOn</c> with resolver-supplied values,
    /// ignoring any value already present on the entity — e.g. to prevent untrusted
    /// request-model binding from spoofing audit fields.
    /// </summary>
    Authoritative = 1
}
