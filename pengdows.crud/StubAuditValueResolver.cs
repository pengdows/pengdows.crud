// =============================================================================
// FILE: StubAuditValueResolver.cs
// PURPOSE: Simple audit value resolver for testing that returns a fixed user ID.
//
// AI SUMMARY:
// - This is a testing utility that provides a fixed user ID for audit columns.
// - The class and constructor are internal (test projects reach it via InternalsVisibleTo).
// - Always returns the same userId that was passed to the constructor.
// - UtcNow is set to DateTime.UtcNow at the moment Resolve() is called.
// - Use in unit tests when you need predictable audit values:
//     var resolver = new StubAuditValueResolver("test-user");
//     var gateway = new TableGateway<Entity, long>(context, resolver);
// - For production, implement a proper AuditValueResolver that extracts
//   the current user from your authentication system.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// A simple audit value resolver that returns a fixed user ID, primarily for testing.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a minimal implementation of <see cref="AuditValueResolver"/>
/// that always returns the same user ID. It is internal and intended for this repository's
/// unit and integration tests, where predictable audit values are needed.
/// </para>
/// <para>
/// For production, implement a resolver that extracts the current user from your
/// authentication context.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // In a unit test:
/// var resolver = new StubAuditValueResolver("test-user-42");
/// var gateway = new TableGateway&lt;Order, long&gt;(context, resolver);
///
/// var order = new Order { Total = 100.00m };
/// await gateway.CreateAsync(order);
/// // order.CreatedBy and order.LastUpdatedBy will be "test-user-42"
/// </code>
/// </example>
/// <seealso cref="AuditValueResolver"/>
internal class StubAuditValueResolver : AuditValueResolver
{
    private readonly object _userId;

    /// <summary>
    /// Initializes a new instance with the specified user ID.
    /// </summary>
    /// <param name="userId">The user ID to return from <see cref="Resolve"/>.</param>
    internal StubAuditValueResolver(object userId)
    {
        _userId = userId;
    }

    /// <inheritdoc />
    /// <returns>
    /// An <see cref="AuditValues"/> instance with the fixed user ID
    /// and the current UTC time.
    /// </returns>
    public override IAuditValues Resolve()
    {
        return new AuditValues
        {
            UserId = _userId,
            UtcNow = DateTime.UtcNow
        };
    }
}