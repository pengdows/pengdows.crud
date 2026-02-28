namespace pengdows.crud;

/// <summary>
/// Resolves <see cref="IAuditValues"/> for the current execution context.
/// </summary>
public interface IAuditValueResolver
{
    /// <summary>
    /// Generates audit values describing the current user and time.
    /// </summary>
    /// <returns>Resolved audit metadata.</returns>
    IAuditValues Resolve();
}