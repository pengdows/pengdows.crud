using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Oracle's MySql.Data, which pengdows.crud does not prepare with, binds parameters by writing
/// backslash-escaped literals into the SQL text, and it decides the escaping when the connection
/// opens, before the session's sql_mode is set. With NO_BACKSLASH_ESCAPES in that sql_mode the server
/// takes the backslashes literally. Live (Aurora MySQL 8.0.42 and Docker MySQL 8.0.46, MySql.Data
/// 9.3): byte[] {0x00, 0xFF} stored as 5C 30 FF, "C:\temp" as "C:\\temp", and "O'Brien" broke the
/// statement - a value can end the literal early. MySqlConnector reads the server's
/// NO_BACKSLASH_ESCAPES status flag and escapes to match, so it keeps the mode. As on 2.0.6 (BP-115).
/// </summary>
public class MySqlNoBackslashEscapesTests
{
    private static SqlDialect OnMySqlData(SupportedDatabase db, bool initialized, out DatabaseContext? context)
    {
        context = null;
        var factory = new fakeDbFactory(db);
        if (initialized)
        {
            context = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
            return (SqlDialect)context.GetDialect();
        }

        return db switch
        {
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, NullLogger.Instance),
            SupportedDatabase.TiDb => new TiDbDialect(factory, NullLogger.Instance),
            _ => new MySqlDialect(factory, NullLogger.Instance, db)
        };
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql, false)]
    [InlineData(SupportedDatabase.AuroraMySql, false)]
    [InlineData(SupportedDatabase.MariaDb, false)]
    [InlineData(SupportedDatabase.TiDb, false)]
    [InlineData(SupportedDatabase.MySql, true)]
    [InlineData(SupportedDatabase.MariaDb, true)]
    [InlineData(SupportedDatabase.TiDb, true)]
    public void OnMySqlData_SessionSettings_OmitNoBackslashEscapes(SupportedDatabase db, bool initialized)
    {
        var dialect = OnMySqlData(db, initialized, out var context);
        try
        {
            foreach (var settings in new[]
                     {
                         dialect.GetBaseSessionSettings(), dialect.GetFinalSessionSettings(false),
                         dialect.GetFinalSessionSettings(true)
                     })
            {
                Assert.DoesNotContain("NO_BACKSLASH_ESCAPES", settings, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("ANSI_QUOTES", settings, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            context?.Dispose();
        }
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    public void OnMySqlConnector_SessionSettings_KeepNoBackslashEscapes(SupportedDatabase db)
    {
        var factory = new pengdows.crud.Tests.MySqlConnector.MinimalConnectorFactory();
        SqlDialect dialect = db == SupportedDatabase.MariaDb
            ? new MariaDbDialect(factory, NullLogger.Instance, isMySqlConnector: true)
            : new MySqlDialect(factory, NullLogger.Instance, isMySqlConnector: true, db);

        Assert.Contains("NO_BACKSLASH_ESCAPES", dialect.GetBaseSessionSettings(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NO_BACKSLASH_ESCAPES", dialect.GetFinalSessionSettings(false), StringComparison.OrdinalIgnoreCase);
    }
}
