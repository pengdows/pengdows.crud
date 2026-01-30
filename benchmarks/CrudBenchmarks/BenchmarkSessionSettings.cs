using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CrudBenchmarks;

internal static class BenchmarkSessionSettings
{
    internal const string SqlServerSessionSettings =
        "SET ANSI_NULLS ON;\n" +
        "SET ANSI_PADDING ON;\n" +
        "SET ANSI_WARNINGS ON;\n" +
        "SET ARITHABORT ON;\n" +
        "SET CONCAT_NULL_YIELDS_NULL ON;\n" +
        "SET QUOTED_IDENTIFIER ON;\n" +
        "SET NUMERIC_ROUNDABORT OFF;";

    internal const string PostgresSessionSettings =
        "SET standard_conforming_strings = on;\n" +
        "SET client_min_messages = warning;";

    internal static async Task ApplyAsync(DbConnection connection, string sql, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed class SessionSettingsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly string _sql;

    public SessionSettingsConnectionInterceptor(string sql)
    {
        _sql = sql;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await BenchmarkSessionSettings.ApplyAsync(connection, _sql, cancellationToken);
    }
}
