using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextInitializationBranchTests
{
    // DatabaseContext.DetectInMemoryKind now delegates to ISqlDialect.DetectInMemoryKind (see
    // pengdows.crud.Tests.dialects.DialectDetectInMemoryKindTests for direct per-dialect coverage
    // of the actual detection rules) rather than owning its own per-product parser, so it's an
    // instance method now — it needs _factory to resolve the dialect.
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=:memory:", "Isolated")]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=file:memdb1?mode=memory&cache=shared", "Shared")]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=test.db", "None")]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=:memory:;cache=shared", "Shared")]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=:memory:", "Isolated")]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=test.duckdb", "None")]
    [InlineData(SupportedDatabase.PostgreSql, "Data Source=:memory:", "None")]
    public void DetectInMemoryKind_HandlesProviders(SupportedDatabase product, string connectionString, string expected)
    {
        var context = CreateContext(connectionString);
        var method = typeof(DatabaseContext).GetMethod(
            "DetectInMemoryKind",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        var result = method!.Invoke(context, new object?[] { product, connectionString });

        Assert.NotNull(result);
        Assert.Equal(expected, result!.ToString());
    }

    private static DatabaseContext CreateContext(string connectionString)
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));
        SetField(context, "_connectionString", connectionString);
        SetField(context, "_logger", NullLogger<IDatabaseContext>.Instance);
        SetField(context, "_factory", new fakeDbFactory(SupportedDatabase.Unknown));
        return context;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}