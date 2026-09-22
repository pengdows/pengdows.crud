using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextModeBranchTests
{
    [Fact]
    public void CoerceMode_HandlesSqliteAndDuckDbMemoryModes()
    {
        var context = CreateContext("Data Source=:memory:");
        var coerce = GetInstanceMethod("CoerceMode");

        var isolated = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Standard, SupportedDatabase.Sqlite, false })!;
        Assert.Equal(DbMode.SingleConnection, isolated);

        var contextShared = CreateContext("Data Source=file:memdb1?mode=memory&cache=shared");
        var shared = (DbMode)coerce.Invoke(contextShared,
            new object?[] { DbMode.Best, SupportedDatabase.Sqlite, false })!;
        Assert.Equal(DbMode.SingleWriter, shared);

        var duckShared = (DbMode)coerce.Invoke(contextShared,
            new object?[] { DbMode.Best, SupportedDatabase.DuckDB, false })!;
        Assert.Equal(DbMode.SingleWriter, duckShared);
    }

    [Fact]
    public void CoerceMode_HandlesFirebirdAndLocalDb()
    {
        var context = CreateContext("ServerType=Embedded;Database=C:\\data\\test.fdb;");
        var coerce = GetInstanceMethod("CoerceMode");

        // Bug fix: embedded Firebird used to be forcibly coerced to SingleConnection regardless
        // of the requested mode. Real testing showed it behaves like an ordinary client-server
        // database — it's now treated as a full server database (see FirebirdDialect.cs), so an
        // explicit Standard request is honored as-is.
        var firebird = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Standard, SupportedDatabase.Firebird, false })!;
        Assert.Equal(DbMode.Standard, firebird);

        var localDb = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Standard, SupportedDatabase.SqlServer, true })!;
        Assert.Equal(DbMode.KeepAlive, localDb);
    }

    [Fact]
    public void CoerceMode_FullServerAndUnknownProviders()
    {
        var context = CreateContext("Server=localhost;Database=test");
        var coerce = GetInstanceMethod("CoerceMode");

        var bestPostgres = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Best, SupportedDatabase.PostgreSql, false })!;
        Assert.Equal(DbMode.Standard, bestPostgres);

        var explicitMode = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.SingleWriter, SupportedDatabase.PostgreSql, false })!;
        Assert.Equal(DbMode.SingleWriter, explicitMode);

        var unknownBest = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Best, SupportedDatabase.Unknown, false })!;
        Assert.Equal(DbMode.Standard, unknownBest);
    }

    [Fact]
    public void WarnOnModeMismatch_ExecutesBranches()
    {
        var context = CreateContext("Data Source=file:test.db");
        var warn = GetInstanceMethod("WarnOnModeMismatch");

        warn.Invoke(context, new object?[] { DbMode.SingleConnection, SupportedDatabase.PostgreSql, false });
        warn.Invoke(context, new object?[] { DbMode.SingleWriter, SupportedDatabase.PostgreSql, false });
        warn.Invoke(context, new object?[] { DbMode.Standard, SupportedDatabase.Sqlite, false });
        warn.Invoke(context, new object?[] { DbMode.SingleConnection, SupportedDatabase.Sybase, false });
    }

    [Fact]
    public void CoerceMode_Db2_TreatedAsFullServerDatabase()
    {
        // Regression guard (structurally guaranteed under the current dialect-delegated
        // architecture, kept as an explicit test anyway): CoerceMode delegates entirely to
        // ISqlDialect.CoerceConnectionMode, so there is no per-database switch left for Db2 (or
        // any other client-server database) to be silently missing from — Db2Dialect inherits the
        // base SqlDialect defaults (Best -> Standard, explicit modes honored as-is) with no
        // special-casing needed. See CLAUDE.md's "Adding a New Database" checklist item 9.
        var context = CreateContext("Server=localhost;Database=test");
        var coerce = GetInstanceMethod("CoerceMode");

        var bestDb2 = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Best, SupportedDatabase.Db2, false })!;
        Assert.Equal(DbMode.Standard, bestDb2);

        var explicitMode = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.SingleWriter, SupportedDatabase.Db2, false })!;
        Assert.Equal(DbMode.SingleWriter, explicitMode);
    }

    [Fact]
    public void IsClientServerDatabase_Db2_ReturnsTrue()
    {
        // Regression: IsClientServerDatabase had no Db2 case, so a misconfigured
        // SingleConnection/SingleWriter mode against Db2 silently got no diagnostic warning
        // that every other client-server database gets.
        var context = CreateContext("Server=localhost;Database=test");
        var method = GetInstanceMethod("IsClientServerDatabase");

        var result = (bool)method.Invoke(context, new object?[] { SupportedDatabase.Db2 })!;

        Assert.True(result);
    }

    private static DatabaseContext CreateContext(string connectionString)
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));
        SetField(context, "_connectionString", connectionString);
        SetField(context, "_logger", NullLogger<IDatabaseContext>.Instance);
        SetField(context, "_factory", new fakeDbFactory(SupportedDatabase.Unknown));
        return context;
    }

    private static MethodInfo GetInstanceMethod(string name)
    {
        var method = typeof(DatabaseContext).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return method!;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
