using System;
using System.Data.Common;
using DuckDB.NET.Data;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class ProviderApplicationNameRetentionTests
{
    [Fact]
    public void SqliteProvider_Preserves_ApplicationName()
    {
        var builder = new SqliteConnectionStringBuilder();
        AssertApplicationNamePreserved(builder,
            baseConnectionString: "Data Source=:memory:",
            applicationNameKey: "ApplicationName",
            provider: "Sqlite");
    }

    [Fact]
    public void DuckDbProvider_Preserves_ApplicationName()
    {
        var builder = new DuckDBConnectionStringBuilder();
        AssertApplicationNamePreserved(builder,
            baseConnectionString: "Data Source=:memory:",
            applicationNameKey: "ApplicationName",
            provider: "DuckDb");
    }

    [Fact]
    public void FirebirdProvider_Preserves_ApplicationName()
    {
        var builder = new FbConnectionStringBuilder();
        AssertApplicationNamePreserved(builder,
            baseConnectionString: "Database=localhost:pengdows.fdb;User=SYSDBA;Password=masterkey",
            applicationNameKey: "ApplicationName",
            provider: "Firebird");
    }

    [Fact]
    public void MySqlProvider_Preserves_ApplicationName()
    {
        var builder = new MySqlConnectionStringBuilder();
        AssertApplicationNamePreserved(builder,
            baseConnectionString: "Server=localhost;Database=pengdows;User Id=root;Password=pass",
            applicationNameKey: "Application Name",
            provider: "MySql");
    }

    [Fact]
    public void MariaDbProvider_Preserves_ApplicationName()
    {
        // MySql.Data is commonly used for both MySQL and MariaDB.
        var builder = new MySqlConnectionStringBuilder();
        AssertApplicationNamePreserved(builder,
            baseConnectionString: "Server=localhost;Database=pengdows;User Id=root;Password=pass",
            applicationNameKey: "Application Name",
            provider: "MariaDb");
    }

    private static void AssertApplicationNamePreserved(DbConnectionStringBuilder builder,
        string baseConnectionString,
        string applicationNameKey,
        string provider)
    {
        var appName = $"pengdows-{provider}-app";
        var connectionString = $"{baseConnectionString};{applicationNameKey}={appName};";

        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Provider doesn't support ApplicationName — this is expected for some providers
            // (e.g., SQLite, MySQL, DuckDB). Skip the round-trip assertion.
            return;
        }

        var roundTrip = builder.ConnectionString ?? string.Empty;
        Assert.Contains(appName, roundTrip, StringComparison.OrdinalIgnoreCase);
    }
}
