using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
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
            new object?[] { DbMode.Standard, SupportedDatabase.Sqlite, false, false })!;
        Assert.Equal(DbMode.SingleConnection, isolated);

        var contextShared = CreateContext("Data Source=file:memdb1?mode=memory&cache=shared");
        var shared = (DbMode)coerce.Invoke(contextShared,
            new object?[] { DbMode.Best, SupportedDatabase.Sqlite, false, false })!;
        Assert.Equal(DbMode.SingleWriter, shared);

        var duckShared = (DbMode)coerce.Invoke(contextShared,
            new object?[] { DbMode.Best, SupportedDatabase.DuckDB, false, false })!;
        Assert.Equal(DbMode.SingleWriter, duckShared);
    }

    [Fact]
    public void CoerceMode_HandlesFirebirdAndLocalDb()
    {
        var context = CreateContext("ServerType=Embedded;Database=C:\\data\\test.fdb;");
        var coerce = GetInstanceMethod("CoerceMode");

        var firebird = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Standard, SupportedDatabase.Firebird, false, true })!;
        Assert.Equal(DbMode.SingleConnection, firebird);

        var localDb = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Standard, SupportedDatabase.SqlServer, true, false })!;
        Assert.Equal(DbMode.KeepAlive, localDb);
    }

    [Fact]
    public void CoerceMode_FullServerAndUnknownProviders()
    {
        var context = CreateContext("Server=localhost;Database=test");
        var coerce = GetInstanceMethod("CoerceMode");

        var bestPostgres = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Best, SupportedDatabase.PostgreSql, false, false })!;
        Assert.Equal(DbMode.Standard, bestPostgres);

        var explicitMode = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.SingleWriter, SupportedDatabase.PostgreSql, false, false })!;
        Assert.Equal(DbMode.SingleWriter, explicitMode);

        var unknownBest = (DbMode)coerce.Invoke(context,
            new object?[] { DbMode.Best, SupportedDatabase.Unknown, false, false })!;
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