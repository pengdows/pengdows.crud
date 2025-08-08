// namespace pengdows.crud;
//
// public class OidcAuditFieldResolver : IAuditFieldResolver
// {
//     private readonly IHttpContextAccessor _context;
//
//     public OidcAuditFieldResolver(IHttpContextAccessor context)
//     {
//         _context = context;
//     }
//
//     public AuditValues Resolve()
//     {
//         var userId = _context.HttpContext?.User?.FindFirst("sub")?.Value ?? "anonymous";
//         return new AuditValues
//         {
//             UserId = userId,
//             UtcNow = DateTime.UtcNow
//         };
//     }
// }

