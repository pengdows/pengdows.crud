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
        // Firebird (embedded or ordinary client-server alike — CoerceMode no longer
        // distinguishes) only auto-selects PreventDatabaseUnload on Best; an explicit Standard
        // request is genuinely safe and honored, unlike LocalDB below.
        var context = CreateContext("ServerType=Embedded;Database=C:\\data\\test.fdb;");
        var coerce = GetInstanceMethod("CoerceMode");

        var firebirdBest = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Best, SupportedDatabase.Firebird, false })!;
        Assert.Equal(DbMode.PreventDatabaseUnload, firebirdBest);

        var firebirdStandard = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Standard, SupportedDatabase.Firebird, false })!;
        Assert.Equal(DbMode.Standard, firebirdStandard);

        var localDb = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Standard, SupportedDatabase.SqlServer, true })!;
        Assert.Equal(DbMode.PreventDatabaseUnload, localDb);
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
    }

    [Fact]
    public void CoerceMode_Db2_TreatedAsFullServerDatabase()
    {
        // Regression: Db2 was missing from the explicit "full server databases" case list,
        // silently falling to the `default` branch. The RESULT was already correct (Standard),
        // but the default branch's LogModeOverride message says "Unknown provider" — misleading
        // for a fully-supported database. Db2 now shares the explicit case with the other
        // client-server databases.
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
