using System;
using Microsoft.Data.Sqlite;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperTypeMapRegistryTests
{
    private sealed class CustomTypeMapRegistry : ITypeMapRegistry
    {
        private readonly TypeMapRegistry _inner = new();
        public ITableInfo GetTableInfo<T>()
        {
            var info = _inner.GetTableInfo<T>();
            info.Name = "custom_" + info.Name;
            return info;
        }
        public void Register<T>() => _inner.Register<T>();
    }

    private sealed class NullTypeMapRegistry : ITypeMapRegistry
    {
        public ITableInfo GetTableInfo<T>() => null!;
        public void Register<T>() { }
    }

    [Fact]
    public void BuildDelete_UsesContextTypeMapRegistry()
    {
        using var ctx = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, new CustomTypeMapRegistry());
        var helper = new EntityHelper<TestTable, long>(ctx);
        var sc = helper.BuildDelete(1, ctx);
        Assert.Contains("custom_test_table", sc.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_MissingTableInfo_Throws()
    {
        using var ctx = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, new NullTypeMapRegistry());
        Assert.Throws<InvalidOperationException>(() => new EntityHelper<TestTable, long>(ctx));
    }
}
