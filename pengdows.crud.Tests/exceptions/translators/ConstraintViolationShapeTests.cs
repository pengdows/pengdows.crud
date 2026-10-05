using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// DRY-018: every translator built its four constraint-violation exceptions by hand (59 copies).
/// DuckDB, Firebird, FlatFile, SQLite and Access passed only the error code, so a violation the
/// dialect recognized by its SQLSTATE (FlatFile and DuckDB: 23505) lost that SQLSTATE, and every
/// constraint name, on the exception. One factory now builds them with every identifier.
/// </summary>
public class ConstraintViolationShapeTests
{
    [Theory]
    [InlineData(SupportedDatabase.DuckDB, "Duplicate key \"id: 1\" violates primary key constraint")]
    [InlineData(SupportedDatabase.FlatFile, "duplicate key")]
    [InlineData(SupportedDatabase.Firebird, "violation of PRIMARY or UNIQUE KEY constraint \"PK_T\" on table \"T\"")]
    [InlineData(SupportedDatabase.Access, "The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship.")]
    [InlineData(SupportedDatabase.PostgreSql, "duplicate key value violates unique constraint \"t_pkey\"")]
    public void UniqueViolation_CarriesTheProvidersSqlState(SupportedDatabase db, string message)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger.Instance);
        var translator = new DbExceptionTranslatorRegistry().Get(db);
        var raw = new NumberedSqlStateDbException(335544665, "23505", message);

        var result = translator.Translate(dialect, raw, DbOperationKind.Insert);

        var violation = Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal("23505", violation.SqlState);
        Assert.Equal(DbExceptionTranslationSupport.TryGetConstraintName(raw), violation.ConstraintName);
        Assert.Equal($"Insert violated a unique constraint on {db}: {message}", violation.Message);
    }
}
