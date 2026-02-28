using System;
using System.Diagnostics;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DatabaseContextReadOnlyConnectionStringTests
{
    [Fact]
    public void ReadOnlyConnectionString_DefaultsToWriterWhenMissing()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=writer;EmulatedProduct=SqlServer",
            ApplicationName = "app-core",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var readerConnectionString = GetReaderConnectionString(ctx);
        var writerConnectionString = ctx.ConnectionString;

        Assert.Contains("Data Source=writer", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplicationIntent=ReadOnly", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Application Name=app-core-ro", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Application Name=app-core-rw", writerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApplicationIntent=ReadOnly", writerConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadOnlyConnectionString_UsesConfiguredValue()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=writer;EmulatedProduct=SqlServer",
            ReadOnlyConnectionString = "Data Source=reader;EmulatedProduct=SqlServer",
            ApplicationName = "app-core",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var readerConnectionString = GetReaderConnectionString(ctx);
        var writerConnectionString = ctx.ConnectionString;

        Assert.Contains("Data Source=reader", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplicationIntent=ReadOnly", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Application Name=app-core-ro", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Application Name=app-core-rw", writerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source=reader", writerConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadOnlyConnectionString_GeneratesDefaultApplicationName_WhenMissing()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=writer;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var readerConnectionString = GetReaderConnectionString(ctx);
        var writerConnectionString = ctx.ConnectionString;
        var expectedApplicationName = GetExpectedDefaultApplicationName();

        Assert.Contains("ApplicationIntent=ReadOnly", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Application Name={expectedApplicationName}-ro", readerConnectionString,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Application Name={expectedApplicationName}-rw", writerConnectionString,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ReadOnlyConnectionString_UsesProvidedValue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(
            "Data Source=writer;EmulatedProduct=SqlServer",
            factory,
            "Data Source=reader;EmulatedProduct=SqlServer");

        var readerConnectionString = GetReaderConnectionString(ctx);

        Assert.Contains("Data Source=reader", readerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplicationIntent=ReadOnly", readerConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetReaderConnectionString(DatabaseContext ctx)
    {
        var field = typeof(DatabaseContext).GetField("_readerConnectionString",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (string)field!.GetValue(ctx)!;
    }

    private static string GetExpectedDefaultApplicationName()
    {
        var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name?.Trim();
        if (!string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            return entryAssemblyName;
        }

        using var process = Process.GetCurrentProcess();
        return string.IsNullOrWhiteSpace(process.ProcessName) ? "pengdows.crud" : process.ProcessName;
    }
}
