// =============================================================================
// FILE: OidcAuditFieldResolver.cs
// PURPOSE: Example/reference implementation of an AuditValueResolver that
//          extracts the user ID from OIDC claims in an ASP.NET Core context.
//
// AI SUMMARY:
// - This file contains a commented-out example implementation showing how to
//   create an AuditValueResolver for web applications using OpenID Connect.
// - It demonstrates extracting the "sub" (subject) claim from HttpContext.User.
// - The code is commented out because it requires ASP.NET Core dependencies
//   (IHttpContextAccessor) which are not included in the core library.
// - To use this pattern:
//   1. Add Microsoft.AspNetCore.Http.Abstractions package
//   2. Uncomment and adapt this code
//   3. Register in DI as scoped or transient
//   4. Inject into TableGateway via constructor
// - Falls back to "anonymous" if no user context is available.
// =============================================================================

// Example implementation - uncomment and add ASP.NET Core references to use:
//
// namespace pengdows.crud;
//
// /// <summary>
// /// Resolves audit values from OIDC claims in an ASP.NET Core HTTP context.
// /// </summary>
// /// <remarks>
// /// This resolver extracts the "sub" (subject) claim from the current user's
// /// identity, which is the standard OIDC claim for user identification.
// /// </remarks>
// /// <example>
// /// <code>
// /// // In Startup.cs or Program.cs:
// /// services.AddHttpContextAccessor();
// /// services.AddScoped&lt;IAuditValueResolver, OidcAuditFieldResolver&gt;();
// /// </code>
// /// </example>
// public class OidcAuditFieldResolver : AuditValueResolver
// {
//     private readonly IHttpContextAccessor _context;
//
//     public OidcAuditFieldResolver(IHttpContextAccessor context)
//     {
//         _context = context;
//     }
//
//     public override IAuditValues Resolve()
//     {
//         var userId = _context.HttpContext?.User?.FindFirst("sub")?.Value ?? "anonymous";
//         return new AuditValues
//         {
//             UserId = userId,
//             UtcNow = DateTime.UtcNow
//         };
//     }
// }
