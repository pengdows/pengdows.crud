using System.Data.Common;
using System.Reflection;

namespace pengdows.crud.Tests;

/// <summary>
/// DataSource is deliberately not exposed anywhere — public or internal — on DatabaseContext
/// (see docs/PRODUCT_THESIS.md principle 5: DbDataSource.CreateConnection() would bypass
/// governor accounting, session settings, and disposal tracking). Tests that need to verify
/// which DbDataSource the constructor wired up read the private field directly via reflection
/// instead of reintroducing a named accessor purely for testability.
/// </summary>
internal static class DatabaseContextTestExtensions
{
    private static readonly FieldInfo DataSourceField =
        typeof(DatabaseContext).GetField("_dataSource", BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static DbDataSource? GetInternalDataSource(this DatabaseContext context)
    {
        return (DbDataSource?)DataSourceField.GetValue(context);
    }
}
