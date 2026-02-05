// =============================================================================
// FILE: AuditValueResolver.cs
// PURPOSE: Abstract base class for resolving audit field values (CreatedBy,
//          LastUpdatedBy, timestamps) during entity CRUD operations.
//
// AI SUMMARY:
// - This is the base class that users extend to provide their own audit value
//   resolution logic (e.g., getting the current user ID from HttpContext,
//   claims, or a DI-injected service).
// - The Resolve() method is called automatically by TableGateway during
//   Create and Update operations to populate audit columns.
// - Implementations should return an IAuditValues instance containing the
//   current timestamp and user identifier.
// - See OidcAuditFieldResolver for an example OIDC-based implementation.
// - See StubAuditValueResolver for a testing stub implementation.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// Abstract base class for resolving audit field values during entity operations.
/// </summary>
/// <remarks>
/// <para>
/// Implement this class to provide custom logic for determining the current user
/// and timestamp for audit columns (CreatedBy, CreatedOn, LastUpdatedBy, LastUpdatedOn).
/// </para>
/// <para>
/// <strong>Usage:</strong> Pass an instance of your implementation to
/// <see cref="TableGateway{TEntity,TRowID}"/> or <see cref="TableGateway{TEntity,TRowID}"/>
/// constructor when working with entities that have audit columns.
/// </para>
/// <example>
/// <code>
/// public class HttpContextAuditResolver : AuditValueResolver
/// {
///     private readonly IHttpContextAccessor _httpContextAccessor;
///
///     public HttpContextAuditResolver(IHttpContextAccessor accessor)
///     {
///         _httpContextAccessor = accessor;
///     }
///
///     public override IAuditValues Resolve()
///     {
///         var userId = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "system";
///         return new AuditValues { UserId = userId };
///     }
/// }
/// </code>
/// </example>
/// </remarks>
/// <seealso cref="IAuditValueResolver"/>
/// <seealso cref="IAuditValues"/>
/// <seealso cref="AuditValues"/>
public abstract class AuditValueResolver : IAuditValueResolver
{
    /// <summary>
    /// Resolves the current audit values including timestamp and user identifier.
    /// </summary>
    /// <returns>
    /// An <see cref="IAuditValues"/> instance containing the current UTC timestamp
    /// and user identifier to use for audit columns.
    /// </returns>
    /// <remarks>
    /// This method is called by <see cref="TableGateway{TEntity,TRowID}"/> during:
    /// <list type="bullet">
    /// <item><description>CreateAsync - to set CreatedBy, CreatedOn, LastUpdatedBy, and LastUpdatedOn</description></item>
    /// <item><description>UpdateAsync - to set LastUpdatedBy and LastUpdatedOn</description></item>
    /// </list>
    /// </remarks>
    public abstract IAuditValues Resolve();
}