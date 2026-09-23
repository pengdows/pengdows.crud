using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// Locks down <see cref="AccessExceptionTranslator"/>. Every message string asserted here was
/// captured live against a real <c>.accdb</c> on pengdows.crud 3.0 (both Microsoft.ACE.OLEDB.12.0
/// and 16.0 — identical wording on both) — see AccessDialect.cs's file-level AI SUMMARY.
/// OleDbException carries no discriminable numeric error code for any violation kind, so every
/// case here is pure message-substring matching, mirroring DuckDbTranslatorTests'/
/// InformixTranslatorTests' shape.
/// </summary>
public class AccessTranslatorTests
{
    private readonly AccessExceptionTranslator _translator = new();

    // ── Connection-level failures ──────────────────────────────────────────────

    [Fact]
    public void MissingFile_Maps_ConnectionException()
    {
        var raw = new SqliteMessageDbException(
            "Could not find file 'C:\\missing.accdb'.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    [Fact]
    public void FileAlreadyOpened_Maps_ConnectionException()
    {
        var raw = new SqliteMessageDbException(
            "Could not use ''; file already opened by user 'Admin' on machine 'OTHER-PC'.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    // ── Lock-wait (Timeout) ─────────────────────────────────────────────────────
    // CONFIRMED live: a genuine lock-WAIT scenario (blocks, then gives up), not a detected
    // circular-wait deadlock.

    [Fact]
    public void CurrentlyLocked_Maps_CommandTimeoutException_IsTransient()
    {
        var raw = new SqliteMessageDbException("Could not update; currently locked.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Update);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.Equal(true, result.IsTransient);
    }

    // ── Read-only violation ─────────────────────────────────────────────────────
    // CONFIRMED live: the exact message a connection opened with "Mode=Read" returns for a write.

    [Fact]
    public void MustUseUpdateableQuery_Maps_ReadOnlyViolationException_NotTransient()
    {
        var raw = new SqliteMessageDbException("Operation must use an updateable query.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Update);

        Assert.IsType<ReadOnlyViolationException>(result);
        Assert.Equal(false, result.IsTransient);
    }

    // ── Constraint-kind classification ─────────────────────────────────────────

    [Fact]
    public void DuplicateValuesMessage_Maps_UniqueConstraintViolationException()
    {
        var raw = new SqliteMessageDbException(
            "The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Theory]
    [InlineData("The record cannot be deleted or changed because table 'order_items' includes related records.")]
    [InlineData("You cannot add or change a record because a related record is required in table 'customers'.")]
    public void RelatedRecordMessage_EitherDirection_Maps_ForeignKeyViolationException(string message)
    {
        var raw = new SqliteMessageDbException(message);

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void MustEnterAValueMessage_Maps_NotNullViolationException()
    {
        var raw = new SqliteMessageDbException("You must enter a value in the 'orders.customer_id' field.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void ValidationRuleMessage_Maps_CheckConstraintViolationException()
    {
        var raw = new SqliteMessageDbException(
            "One or more values are prohibited by the validation rule 'quantity > 0' set for 'order_items.quantity'.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    // ── Precedence: lock-wait / read-only checked before constraint matching ────

    [Fact]
    public void CurrentlyLockedMessage_NotShadowedByConstraintMatching()
    {
        // None of the four constraint messages contain "currently locked", so this only proves
        // the translator's actual check order doesn't accidentally invert.
        var raw = new SqliteMessageDbException("Could not update; currently locked.");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Update);

        Assert.IsNotType<UniqueConstraintViolationException>(result);
        Assert.IsType<CommandTimeoutException>(result);
    }

    // ── Fallback ────────────────────────────────────────────────────────────────

    [Fact]
    public void UnrecognizedMessage_Maps_DatabaseOperationException()
    {
        var raw = new SqliteMessageDbException("some unexpected Jet/ACE error");

        var result = _translator.Translate(SupportedDatabase.Access, raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
        Assert.IsNotType<ForeignKeyViolationException>(result);
    }
}
