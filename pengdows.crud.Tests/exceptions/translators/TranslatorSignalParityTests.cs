using System;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// Every signal the 2.0.6 translators matched directly before classification moved into the
/// dialects (3.0's IsXxxViolation / TryClassifyProviderException) must still produce the same
/// typed exception through the registry.
/// </summary>
public class TranslatorSignalParityTests
{
    public static TheoryData<SupportedDatabase, string, string, string, Type> Signals() => new()
    {
        // Db2: SQLCODEs (sign-insensitive) and SQLSTATEs.
        { SupportedDatabase.Db2, "code", "-803", "duplicate", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.Db2, "code", "-407", "null", typeof(NotNullViolationException) },
        { SupportedDatabase.Db2, "code", "-545", "check", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.Db2, "code", "-530", "fk", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Db2, "code", "-531", "fk", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Db2, "code", "-532", "fk", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Db2, "code", "-911", "lock", typeof(SerializationConflictException) },
        { SupportedDatabase.Db2, "code", "-913", "lock", typeof(SerializationConflictException) },
        { SupportedDatabase.Db2, "state", "23504", "fk", typeof(ForeignKeyViolationException) },
        // MySQL family.
        { SupportedDatabase.MySql, "code", "1062", "dup", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.MySql, "code", "1169", "dup", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.MySql, "code", "1205", "lock wait", typeof(CommandTimeoutException) },
        { SupportedDatabase.MySql, "code", "4025", "check", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.MySql, "code", "1213", "deadlock", typeof(DeadlockException) },
        // Oracle.
        { SupportedDatabase.Oracle, "code", "1", "unique", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.Oracle, "code", "1400", "null", typeof(NotNullViolationException) },
        { SupportedDatabase.Oracle, "code", "2290", "check", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.Oracle, "code", "2291", "fk", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Oracle, "code", "2292", "fk", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Oracle, "code", "60", "deadlock", typeof(DeadlockException) },
        { SupportedDatabase.Oracle, "code", "8177", "serialize", typeof(SerializationConflictException) },
        // SQL Server.
        { SupportedDatabase.SqlServer, "code", "2601", "dup", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.SqlServer, "code", "2627", "dup", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.SqlServer, "code", "-2", "Execution Timeout Expired", typeof(CommandTimeoutException) },
        { SupportedDatabase.SqlServer, "code", "547", "The INSERT statement conflicted with the CHECK constraint", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.SqlServer, "code", "547", "The INSERT statement conflicted with the FOREIGN KEY constraint", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.SqlServer, "code", "515", "Cannot insert the value NULL", typeof(NotNullViolationException) },
        { SupportedDatabase.SqlServer, "code", "1205", "deadlock victim", typeof(DeadlockException) },
        // SQLite.
        { SupportedDatabase.Sqlite, "code", "8", "attempt to write a readonly database", typeof(ReadOnlyViolationException) },
        { SupportedDatabase.Sqlite, "msg", "", "UNIQUE constraint failed: t.x", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.Sqlite, "msg", "", "FOREIGN KEY constraint failed", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Sqlite, "msg", "", "NOT NULL constraint failed: t.x", typeof(NotNullViolationException) },
        { SupportedDatabase.Sqlite, "msg", "", "CHECK constraint failed: ck", typeof(CheckConstraintViolationException) },
        // PostgreSQL family.
        { SupportedDatabase.PostgreSql, "state", "23505", "dup", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.PostgreSql, "state", "40P01", "deadlock", typeof(DeadlockException) },
        { SupportedDatabase.PostgreSql, "state", "40001", "serialize", typeof(SerializationConflictException) },
        { SupportedDatabase.PostgreSql, "state", "57014", "canceling statement due to statement timeout", typeof(CommandTimeoutException) },
        // Spanner (PostgreSQL translator, message-based).
        { SupportedDatabase.Spanner, "msg", "", "Column x must not be NULL in table t", typeof(NotNullViolationException) },
        { SupportedDatabase.Spanner, "msg", "", "Check constraint `ck` is violated for key (1)", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.Spanner, "msg", "", "Foreign key constraint violation when inserting: referenced row missing", typeof(ForeignKeyViolationException) },
        // Firebird (message-based).
        { SupportedDatabase.Firebird, "msg", "", "violation of PRIMARY or UNIQUE KEY constraint \"PK\" on table \"T\"", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.Firebird, "msg", "", "violation of FOREIGN KEY constraint \"FK\" on table \"T\"", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Firebird, "msg", "", "validation error for column \"X\", value \"*** null ***\"", typeof(NotNullViolationException) },
        { SupportedDatabase.Firebird, "msg", "", "Operation violates CHECK constraint CK on view or table T", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.Firebird, "msg", "", "update conflicts with concurrent update", typeof(SerializationConflictException) },
        // InterBase (numeric GDS codes).
        { SupportedDatabase.InterBase, "code", "335544665", "unique", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.InterBase, "code", "335544347", "null", typeof(NotNullViolationException) },
        { SupportedDatabase.InterBase, "code", "335544558", "check", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.InterBase, "code", "335544466", "fk", typeof(ForeignKeyViolationException) },
        // DuckDB.
        { SupportedDatabase.DuckDB, "state", "25006", "read-only", typeof(ReadOnlyViolationException) },
        { SupportedDatabase.DuckDB, "msg", "", "Constraint Error: Duplicate key \"id: 1\" violates primary key constraint", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.DuckDB, "msg", "", "Constraint Error: Violates foreign key constraint", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.DuckDB, "msg", "", "Constraint Error: NOT NULL constraint failed: t.x", typeof(NotNullViolationException) },
        { SupportedDatabase.DuckDB, "msg", "", "Constraint Error: CHECK constraint failed: t", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.DuckDB, "msg", "", "Invalid Input Error: Cannot execute statement in read-only mode", typeof(ReadOnlyViolationException) },
        // HANA (NativeError).
        { SupportedDatabase.SapHana, "code", "301", "unique", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.SapHana, "code", "287", "null", typeof(NotNullViolationException) },
        { SupportedDatabase.SapHana, "code", "677", "check", typeof(CheckConstraintViolationException) },
        { SupportedDatabase.SapHana, "code", "461", "fk", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.SapHana, "code", "462", "fk", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.SapHana, "code", "133", "deadlock", typeof(DeadlockException) },
        { SupportedDatabase.SapHana, "code", "131", "lock wait timeout", typeof(CommandTimeoutException) },
        { SupportedDatabase.SapHana, "code", "129", "read-only", typeof(ReadOnlyViolationException) },
        // Access (message-based).
        { SupportedDatabase.Access, "msg", "", "Could not update; currently locked.", typeof(CommandTimeoutException) },
        { SupportedDatabase.Access, "msg", "", "Operation must use an updateable query.", typeof(ReadOnlyViolationException) },
        { SupportedDatabase.Access, "msg", "", "The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship.", typeof(UniqueConstraintViolationException) },
        { SupportedDatabase.Access, "msg", "", "You cannot add or change a record because a related record is required in table 'P'.", typeof(ForeignKeyViolationException) },
        { SupportedDatabase.Access, "msg", "", "You must enter a value in the 'T.X' field.", typeof(NotNullViolationException) },
        { SupportedDatabase.Access, "msg", "", "One or more values are prohibited by the validation rule 'x>0' set for 'T'.", typeof(CheckConstraintViolationException) },
    };

    private static DbException Raw(string kind, string value, string message) => kind switch
    {
        "code" => new NumberedDbException(int.Parse(value), message),
        "state" => new SqlStateDbException(value, message),
        _ => new SqliteMessageDbException(message)
    };

    [Theory]
    [MemberData(nameof(Signals))]
    public void PreviouslyTranslatedSignal_StillMapsToSameType(SupportedDatabase db, string kind, string value,
        string message, Type expected)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger.Instance);
        var translator = new DbExceptionTranslatorRegistry().Get(db);

        var result = translator.Translate(dialect, Raw(kind, value, message), DbOperationKind.Insert);

        Assert.IsType(expected, result);
    }
}
