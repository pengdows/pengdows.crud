using System;
using System.Data;
using pengdows.crud.enums;

namespace pengdows.crud.@internal;

/// <summary>
/// Service for configuring database session settings.
/// Provides database-specific session initialization SQL.
/// </summary>
internal static class SessionSettingsConfigurator
{
    private const string MySqlAnsiQuotesMode =
        "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE," +
        "ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';\n";

    /// <summary>
    /// Gets the session settings SQL for a specific database product and mode.
    /// </summary>
    public static string GetSessionSettings(SupportedDatabase product, DbMode mode)
    {
        return product switch
        {
            SupportedDatabase.MySql or SupportedDatabase.MariaDb =>
                MySqlAnsiQuotesMode,

            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb =>
                @"SET standard_conforming_strings = on;
SET client_min_messages = warning;
",

            SupportedDatabase.Oracle =>
                @"ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';
",

            SupportedDatabase.Sqlite =>
                "PRAGMA foreign_keys = ON;",

            SupportedDatabase.Firebird =>
                // Firebird settings go in connection string, not session
                string.Empty,

            _ => string.Empty
        };
    }

    /// <summary>
    /// Determines whether session settings should be applied based on settings content and mode.
    /// </summary>
    public static bool ShouldApplySettings(string? settings, DbMode mode)
    {
        return !string.IsNullOrWhiteSpace(settings);
    }

    /// <summary>
    /// Applies session settings to a database connection.
    /// Splits on semicolons and executes each statement individually.
    /// Returns true if successful, false if an error occurred.
    /// </summary>
    public static bool ApplySessionSettings(IDbConnection connection, string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return true;
        }

        try
        {
            // Split on ';' and execute each non-empty statement individually
            var parts = settings.Split(';');
            foreach (var part in parts)
            {
                var stmt = part.Trim();
                if (string.IsNullOrEmpty(stmt))
                {
                    continue;
                }

                using var cmd = connection.CreateCommand();
                cmd.CommandText = stmt; // do not append ';'
                cmd.ExecuteNonQuery();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
