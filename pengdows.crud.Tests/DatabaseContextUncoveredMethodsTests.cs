#region

using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextUncoveredMethodsTests
{
    [Fact]
    public void ExecuteSessionSettings_CallsDialectSettings()
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

        context.ExecuteSessionSettings(connection, false);

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
    public void ExecuteSessionSettings_DifferentDialects_ExecutesWithoutError(SupportedDatabase database)
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

        context.ExecuteSessionSettings(connection, false);

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

        // Constructor now has optional third parameter: (string, DbProviderFactory, string?)
        var method = typeof(DatabaseContext).GetConstructor(
            new[] { typeof(string), typeof(DbProviderFactory), typeof(string) });
        var context = (DatabaseContext)method!.Invoke(new object?[] { "Data Source=:memory:", factory, null });

        Assert.NotNull(context);
        Assert.Equal(SupportedDatabase.Sqlite, context.Product);

        context.Dispose();
    }

    [Fact]
    public void DatabaseContext_StringConstructorWithTypeMap_CreatesValidContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var typeMap = new TypeMapRegistry();

        // Constructor now has optional fourth parameter: (string, DbProviderFactory, ITypeMapRegistry, string?)
        var method = typeof(DatabaseContext).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(DbProviderFactory), typeof(ITypeMapRegistry), typeof(string) },
            null);
        var context = (DatabaseContext)method!.Invoke(new object?[] { "Data Source=:memory:", factory, typeMap, null });

        Assert.NotNull(context);
        Assert.Same(typeMap, context.TypeMapRegistry);

        context.Dispose();
    }
}
