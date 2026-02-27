using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class ReadWriteConnectionStringSeparationTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> ConnectionStrings { get; } = new();

        [AllowNull]
        public override string ConnectionString
        {
            get => base.ConnectionString;
            set
            {
                var normalized = value ?? string.Empty;
                ConnectionStrings.Add(normalized);
                base.ConnectionString = normalized;
            }
        }
    }

    private sealed class RecordingFactory : DbProviderFactory
    {
        public List<RecordingConnection> Connections { get; } = new();

        public override DbConnection CreateConnection()
        {
            var conn = new RecordingConnection { EmulatedProduct = SupportedDatabase.SqlServer };
            Connections.Add(conn);
            return conn;
        }

        public override DbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new fakeDbParameter();
        }
    }

    private static DatabaseContext CreateContext(RecordingFactory factory, string? applicationName = "pengdows-test")
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            ApplicationName = applicationName ?? string.Empty
        };

        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task Standard_ReadAndWrite_UseDistinctApplicationNameSuffixes()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateContext(factory);

        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        ctx.CloseAndDisposeConnection(readConn);

        Assert.NotEmpty(factory.Connections);
        var readConnection = factory.Connections[^1];
        var readCs = readConnection.ConnectionStrings[^1];

        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        ctx.CloseAndDisposeConnection(writeConn);

        var writeConnection = factory.Connections[^1];
        var writeCs = writeConnection.ConnectionStrings[^1];

        Assert.Contains("Application Name", readCs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Application Name", writeCs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-ro", readCs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-rw", writeCs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplicationIntent=ReadOnly", readCs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApplicationIntent=ReadOnly", writeCs, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(readCs, writeCs);
    }

    [Fact]
    public async Task Standard_ReadAndWrite_GenerateDefaultApplicationNameSuffixes_WhenMissing()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateContext(factory, string.Empty);

        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        ctx.CloseAndDisposeConnection(readConn);

        var readConnection = factory.Connections[^1];
        var readCs = readConnection.ConnectionStrings[^1];

        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        ctx.CloseAndDisposeConnection(writeConn);

        var writeConnection = factory.Connections[^1];
        var writeCs = writeConnection.ConnectionStrings[^1];
        var expectedApplicationName = GetExpectedDefaultApplicationName();

        Assert.Contains($"Application Name={expectedApplicationName}-ro", readCs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Application Name={expectedApplicationName}-rw", writeCs, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(readCs, writeCs);
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
