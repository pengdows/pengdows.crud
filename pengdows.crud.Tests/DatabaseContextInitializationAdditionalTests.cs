using System;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextInitializationAdditionalTests
{
    [Fact]
    public void Constructor_UsesFactoryStringDataSourceWhenAvailable()
    {
        var factory = new StringDataSourceFactory();
        using var ctx = new DatabaseContext("Data Source=:memory:", factory);

        Assert.NotNull(ctx.DataSource);
        Assert.IsType<TestDataSource>(ctx.DataSource);
    }

    [Fact]
    public void Constructor_IgnoresDataSourceCreationFailures()
    {
        var factory = new ThrowingDataSourceFactory();
        using var ctx = new DatabaseContext("Data Source=:memory:", factory);

        Assert.Null(ctx.DataSource);
    }

    [Fact]
    public void SetConnectionString_FirstAssignment_Succeeds()
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));
        var setMethod =
            typeof(DatabaseContext).GetMethod("SetConnectionString", BindingFlags.NonPublic | BindingFlags.Instance);
        var field = typeof(DatabaseContext).GetField("_connectionString",
            BindingFlags.NonPublic | BindingFlags.Instance);

        setMethod!.Invoke(context, new object?[] { "Data Source=test" });

        Assert.Equal("Data Source=test", field!.GetValue(context));
    }

    [Fact]
    public void SetConnectionString_WhenAlreadySet_Throws()
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));
        var field = typeof(DatabaseContext).GetField("_connectionString",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var setMethod =
            typeof(DatabaseContext).GetMethod("SetConnectionString", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(context, "Data Source=existing");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            setMethod!.Invoke(context, new object?[] { "Data Source=new" }));

        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Theory]
    [InlineData("Data Source=:memory:", true)]
    [InlineData("Data Source=test.db", false)]
    public void IsMemoryDataSource_DetectsMemoryConnectionStrings(string connectionString, bool expected)
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));
        var field = typeof(DatabaseContext).GetField("_connectionString",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var method =
            typeof(DatabaseContext).GetMethod("IsMemoryDataSource", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(context, connectionString);

        var result = (bool)method!.Invoke(context, Array.Empty<object>())!;

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReaderConnectionString_UsesBaseConnectionString_WhenNoReadOnlyParameter()
    {
        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;",
            DbMode = DbMode.SingleWriter,
            ApplicationName = "app"
        };
        var factory = new fakeDbFactory(SupportedDatabase.MySql);

        using var ctx = new DatabaseContext(configuration, factory, NullLoggerFactory.Instance);

        var field = typeof(DatabaseContext).GetField("_readerConnectionString",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var readerConnectionString = (string)field!.GetValue(ctx)!;

        Assert.Contains("Server=test", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Application Name=app:ro", readerConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StringDataSourceFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection()
        {
            return new fakeDbConnection();
        }

        public override DbDataSource CreateDataSource(string connectionString)
        {
            return new TestDataSource(connectionString);
        }
    }

    private sealed class ThrowingDataSourceFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection()
        {
            return new fakeDbConnection();
        }

        public override DbDataSource CreateDataSource(string connectionString)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class TestDataSource : DbDataSource
    {
        private readonly string _connectionString;

        public TestDataSource(string connectionString)
        {
            _connectionString = connectionString;
        }

        public override string ConnectionString => _connectionString;

        protected override DbConnection CreateDbConnection()
        {
            var connection = new fakeDbConnection();
            connection.ConnectionString = _connectionString;
            return connection;
        }
    }
}
