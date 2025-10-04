using System;
using pengdows.crud.enums;

namespace pengdows.crud.@internal;

internal static class DatabaseProductDetector
{
    private static readonly (SupportedDatabase Product, string[] Tokens)[] SchemaProductTokens =
    {
        (SupportedDatabase.SqlServer, new[] { "sql server" }),
        (SupportedDatabase.MariaDb, new[] { "mariadb" }),
        (SupportedDatabase.MySql, new[] { "mysql" }),
        (SupportedDatabase.CockroachDb, new[] { "cockroach" }),
        (SupportedDatabase.PostgreSql, new[] { "postgres" }),
        (SupportedDatabase.Oracle, new[] { "oracle" }),
        (SupportedDatabase.Sqlite, new[] { "sqlite" }),
        (SupportedDatabase.Firebird, new[] { "firebird" }),
        (SupportedDatabase.DuckDB, new[] { "duckdb", "duck db" })
    };

    private static readonly (SupportedDatabase Product, string[] Tokens)[] FactoryTypeTokens =
    {
        (SupportedDatabase.SqlServer, new[] { "sqlserver", "system.data.sqlclient", "microsoft.data.sqlclient" }),
        (SupportedDatabase.PostgreSql, new[] { "npgsql", "postgres" }),
        (SupportedDatabase.MySql, new[] { "mysql" }),
        (SupportedDatabase.MariaDb, new[] { "mariadb" }),
        (SupportedDatabase.Sqlite, new[] { "sqlite" }),
        (SupportedDatabase.Oracle, new[] { "oracle" }),
        (SupportedDatabase.Firebird, new[] { "firebird" }),
        (SupportedDatabase.DuckDB, new[] { "duckdb" })
    };

    public static SupportedDatabase FromProductName(string? productName)
    {
        return Match(productName, SchemaProductTokens);
    }

    public static SupportedDatabase FromFactoryTypeName(string? typeName)
    {
        return Match(typeName, FactoryTypeTokens);
    }

    private static SupportedDatabase Match(string? source, (SupportedDatabase Product, string[] Tokens)[] tokenSets)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return SupportedDatabase.Unknown;
        }

        foreach (var (product, tokens) in tokenSets)
        {
            foreach (var token in tokens)
            {
                if (source.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return product;
                }
            }
        }

        return SupportedDatabase.Unknown;
    }
}
