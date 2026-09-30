using System;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Review 2026-09-29 (silent failure paths): FirebirdDialect.ResetConnectionPoolForDdl swallowed
/// every failure with a bare catch. A missed reset surfaces later as a confusing "object ... is in
/// use" on the DDL, so the reset stays best-effort but the failure is logged.
/// </summary>
public sealed class FirebirdPoolResetLoggingTests
{
    [Fact]
    public void ResetConnectionPoolForDdl_Failure_DoesNotThrowAndIsLogged()
    {
        var logs = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { logs });
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.Firebird, factory,
            loggerFactory.CreateLogger<SqlDialect>());
        factory.ThrowOnCreateConnection = new InvalidOperationException("pool reset failed");

        dialect.ResetConnectionPoolForDdl("Database=/data/x.fdb");

        Assert.Contains(logs.Entries, e => e.Level >= LogLevel.Debug && e.Exception?.Message == "pool reset failed");
    }
}
