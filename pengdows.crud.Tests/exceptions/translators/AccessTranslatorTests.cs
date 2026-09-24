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
/// Tests for <see cref="AccessExceptionTranslator"/>. Unlike Sybase's <c>AseException</c>
/// (deliberately excluded from dialect delegation because it isn't a <see cref="DbException"/> at
/// all), <c>OleDbException</c> IS a real <see cref="DbException"/>, so this translator follows the
/// standard unified pattern: delegate constraint-kind classification to
/// <c>AccessDialect</c>'s <c>IsXxxViolation</c> overrides rather than re-deriving message matches
/// here. Every message text below was captured live against a real <c>.accdb</c> — see
/// AccessDialect.cs's file-level AI SUMMARY.
/// </summary>
public class AccessTranslatorTests
{
    private readonly AccessExceptionTranslator _translator = new();

    private static ISqlDialect TestDialect() =>
        SqlDialectFactory.CreateDialectForType(SupportedDatabase.Access, new fakeDbFactory(SupportedDatabase.Access), NullLogger.Instance);

    [Fact]
    public void UniqueViolation_DuplicateIndexMessage_Maps_UniqueConstraintViolationException()
    {
        var raw = new PlainMessageDbException(
            "The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship. Change the data in the field or fields that contain duplicate data, remove the index, or redefine the index to permit duplicate entries and try again.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void NotNullViolation_MustEnterValueMessage_Maps_NotNullViolationException()
    {
        var raw = new PlainMessageDbException("You must enter a value in the 'parent_t.name' field.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void CheckViolation_ValidationRuleMessage_Maps_CheckConstraintViolationException()
    {
        var raw = new PlainMessageDbException(
            "One or more values are prohibited by the validation rule 'chk_age' set for 'parent_t'. Enter a value that the expression for this field can accept.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void ForeignKeyViolation_InsertBlockedByMissingParent_Maps_ForeignKeyViolationException()
    {
        var raw = new PlainMessageDbException(
            "You cannot add or change a record because a related record is required in table 'parent_t'.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void ForeignKeyViolation_DeleteBlockedByChildRow_Maps_ForeignKeyViolationException()
    {
        // Different wording from the insert-blocked case above — confirmed live this session,
        // the exact SQL Server pitfall CLAUDE.md's checklist item 22 warns about.
        var raw = new PlainMessageDbException(
            "The record cannot be deleted or changed because table 'child_t' includes related records.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void MissingFileException_Maps_ConnectionException()
    {
        // Exact message captured live this session: attempting to open a .accdb that doesn't
        // exist at all.
        var raw = new PlainMessageDbException(
            @"Could not find file 'C:\some\path\does_not_exist.accdb'.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void ExclusivelyLockedFileException_Maps_ConnectionException()
    {
        // Exact message captured live this session: attempting to open a .accdb another
        // connection/process already holds exclusively.
        var raw = new PlainMessageDbException(
            "You attempted to open a database that is already opened by user 'Admin' on machine 'DESKTOP-M1TPL8B'. Try again when the database is available.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void LockWaitException_CurrentlyLockedMessage_Maps_CommandTimeoutException()
    {
        // See AccessDialectTests.AnalyzeException_CurrentlyLockedMessage_ClassifiesAsTimeout for
        // the live-verified root cause this message comes from.
        var raw = new PlainMessageDbException("Could not update; currently locked.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.True(result.IsTransient);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void ReadOnlyViolation_UpdateableQueryMessage_Maps_ReadOnlyViolationException()
    {
        // CONFIRMED live this session: opening a real .accdb with "Mode=Read" in the connection
        // string (AccessDialect.GetReadOnlyConnectionParameter) genuinely enforces read-only at
        // the driver level — an attempted write against that connection fails with exactly this
        // message. Mirrors SqliteDialect/DuckDbDialect's ReadOnlyViolation classification, adapted
        // to message-substring matching since OleDbException carries no discriminable error code.
        var raw = new PlainMessageDbException("Operation must use an updateable query.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<ReadOnlyViolationException>(result);
        Assert.False(result.IsTransient);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    [Fact]
    public void UnrelatedException_MapsToFallback()
    {
        var raw = new PlainMessageDbException("Syntax error in query. Incomplete query clause.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsNotType<UniqueConstraintViolationException>(result);
        Assert.IsNotType<ForeignKeyViolationException>(result);
        Assert.IsNotType<NotNullViolationException>(result);
        Assert.IsNotType<CheckConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Access, result.Database);
    }

    // Kept from 2.0.6 (not in 3.0): the lock-wait check must not be shadowed by constraint matching.
    [Fact]
    public void CurrentlyLockedMessage_NotShadowedByConstraintMatching()
    {
        var raw = new PlainMessageDbException("Could not update; currently locked.");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Update);

        Assert.IsNotType<UniqueConstraintViolationException>(result);
        Assert.IsType<CommandTimeoutException>(result);
    }
}
