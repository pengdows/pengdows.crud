using System;
using System.Reflection;
using AdoNetCore.AseClient;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// REV-050: a write the database refuses because the transaction, session or database is
/// read-only must surface as ReadOnlyViolationException. The codes below were captured live on
/// 2026-10-03 (writing through a read-only transaction or into a read-only database), except where
/// marked "documented". Before this, only SQLite, DuckDB, FlatFile, Access and HANA translated them.
/// </summary>
public sealed class ReadOnlyViolationTranslationTests
{
    private static readonly DbExceptionTranslatorRegistry Registry = new();

    private static DatabaseException Translate(SupportedDatabase database, Exception raw) =>
        Registry.Get(database).Translate(
            SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance),
            raw, DbOperationKind.Insert);

    public static TheoryData<SupportedDatabase, Func<Exception>> ReadOnlyErrors() => new()
    {
        { SupportedDatabase.PostgreSql, () => new SqlStateDbException("25006", "25006: cannot execute INSERT in a read-only transaction") },
        { SupportedDatabase.CockroachDb, () => new SqlStateDbException("25006", "25006: cannot execute INSERT in a read-only transaction") },
        { SupportedDatabase.YugabyteDb, () => new SqlStateDbException("25006", "25006: cannot execute INSERT in a read-only transaction") },
        { SupportedDatabase.Spanner, () => new SqlStateDbException("P0001", "P0001: Analyzing updates is not allowed for read-only transactions") },
        { SupportedDatabase.MySql, () => new NumberedDbException(1792, "Cannot execute statement in a READ ONLY transaction.") },
        { SupportedDatabase.MariaDb, () => new NumberedDbException(1792, "Cannot execute statement in a READ ONLY transaction") },
        { SupportedDatabase.MySql, () => new NumberedDbException(1290, "The MySQL server is running with the --super-read-only option so it cannot execute this statement") },
        { SupportedDatabase.MySql, () => new NumberedDbException(1290, "The MySQL server is running with the --read-only option so it cannot execute this statement") },
        { SupportedDatabase.TiDb, () => new NumberedDbException(1836, "Running in read-only mode") },
        { SupportedDatabase.SqlServer, () => new NumberedDbException(3906, "Failed to update database \"db\" because the database is read-only.") },
        { SupportedDatabase.Oracle, () => new NumberedDbException(1456, "ORA-01456: may not perform insert, delete, update operation inside a READ ONLY transaction") },
        // documented: ORA-16000 database or pluggable database open for read-only access
        { SupportedDatabase.Oracle, () => new NumberedDbException(16000, "ORA-16000: database or pluggable database open for read-only access") },
        { SupportedDatabase.Informix, () => new NumberedDbException(-878, "Invalid operation for a READ-ONLY transaction.") },
        { SupportedDatabase.Firebird, () => new NumberedDbException(335544361, "attempted update during read-only transaction") },
        // documented: isc_read_only_database
        { SupportedDatabase.Firebird, () => new NumberedDbException(335544765, "attempted update on read-only database") },
        { SupportedDatabase.InterBase, () => new NumberedDbException(335544361, "attempted update during read-only transaction") },
        // documented: SQL0817N, an update prohibited (e.g. a read-only HADR standby)
        { SupportedDatabase.Db2, () => new NumberedDbException(-817, "SQL0817N The SQL statement cannot be executed because the statement will result in a prohibited update operation.") },
        // documented: ASE 3906, database is READ ONLY
        { SupportedDatabase.SybaseASE, () => Ase(3906, "Attempt to BEGIN TRANSACTION in database 'db' failed because database is READ ONLY.") }
    };

    [Theory]
    [MemberData(nameof(ReadOnlyErrors))]
    public void ReadOnlyRefusal_IsReadOnlyViolationException(SupportedDatabase database, Func<Exception> raw)
    {
        Assert.IsType<ReadOnlyViolationException>(Translate(database, raw()));
    }

    // 1290 is "option prevents statement" for any server option; only the read-only ones count.
    [Fact]
    public void MySql1290_ForAnotherOption_IsNotAReadOnlyViolation()
    {
        var raw = new NumberedDbException(1290,
            "The MySQL server is running with the --secure-file-priv option so it cannot execute this statement");

        Assert.IsNotType<ReadOnlyViolationException>(Translate(SupportedDatabase.MySql, raw));
    }

    private static AseException Ase(int messageNumber, string message)
    {
        var error = new AseError();
        Set(error, nameof(AseError.MessageNumber), messageNumber);
        Set(error, nameof(AseError.Message), message);
        return new AseException(new[] { error });
    }

    private static void Set(object target, string name, object? value) =>
        target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!.Invoke(target, new[] { value });
}
