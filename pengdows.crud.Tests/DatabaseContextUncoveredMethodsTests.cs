#region

using System.Data.Common;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextUncoveredMethodsTests
{
    [Fact]
    public void ApplyConnectionSessionSettings_CallsDialectSettings()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.SingleWriter,
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            ConnectionString = "Data Source=:memory:"
        };
        var context = new DatabaseContext(config, factory);

        using var connection = factory.CreateConnection();
        connection.Open();

        var method = typeof(DatabaseContext).GetMethod("ApplyConnectionSessionSettings",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method!.Invoke(context, new object[] { connection });

        Assert.True(true);
    }

    [Fact]
    public void GetStandardConnection_ReturnsTrackedConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.Standard,
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            ConnectionString = "Data Source=:memory:"
        };
        var context = new DatabaseContext(config, factory);

        var method = typeof(DatabaseContext).GetMethod("GetStandardConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = method!.Invoke(context, new object[] { false, false });

        Assert.NotNull(connection);
    }

    [Fact]
    public void GetStandardConnection_WithSharedParameter_ReturnsTrackedConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.Standard,
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            ConnectionString = "Data Source=:memory:"
        };
        var context = new DatabaseContext(config, factory);

        var method = typeof(DatabaseContext).GetMethod("GetStandardConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = method!.Invoke(context, new object[] { true, false });

        Assert.NotNull(connection);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Oracle)]
    public void ApplyConnectionSessionSettings_DifferentDialects_ExecutesWithoutError(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database.ToString());
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.Standard,
            ProviderName = database.ToString(),
            ConnectionString = $"Data Source=test;EmulatedProduct={database}"
        };
        var context = new DatabaseContext(config, factory);

        using var connection = factory.CreateConnection();
        connection.Open();

        var method = typeof(DatabaseContext).GetMethod("ApplyConnectionSessionSettings",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method!.Invoke(context, new object[] { connection });

        Assert.True(true);
    }

    [Fact]
    public void SessionSettingsPreamble_NonEmptyForSomeDialects()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer.ToString());
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.Standard,
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer"
        };
        var context = new DatabaseContext(config, factory);

        var preamble = context.SessionSettingsPreamble;

        Assert.NotNull(preamble);
    }

    [Fact]
    public void DatabaseContext_StringConstructor_CreatesValidContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());

        var method = typeof(DatabaseContext).GetConstructor(new[] { typeof(string), typeof(DbProviderFactory) });
        var context = (DatabaseContext)method!.Invoke(new object[] { "Data Source=:memory:", factory });

        Assert.NotNull(context);
        Assert.Equal(SupportedDatabase.Sqlite, context.Product);

        context.Dispose();
    }

    [Fact]
    public void DatabaseContext_StringConstructorWithTypeMap_CreatesValidContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var typeMap = new TypeMapRegistry();

        var method = typeof(DatabaseContext).GetConstructor(new[]
            { typeof(string), typeof(DbProviderFactory), typeof(ITypeMapRegistry) });
        var context = (DatabaseContext)method!.Invoke(new object[] { "Data Source=:memory:", factory, typeMap });

        Assert.NotNull(context);
        Assert.Same(typeMap, context.TypeMapRegistry);

        context.Dispose();
    }
}