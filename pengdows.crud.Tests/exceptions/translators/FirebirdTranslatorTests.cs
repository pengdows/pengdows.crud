using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// Tests for FirebirdExceptionTranslator constraint detection.
/// Firebird uses message-based detection (ISC codes require fragile reflection across provider
/// versions). Detection order: constraint patterns BEFORE LooksLikeTimeout — a row value that
/// contains "timeout" must not be misclassified as CommandTimeoutException.
/// </summary>
public class FirebirdTranslatorTests
{
    private readonly FirebirdExceptionTranslator _translator = new();

    // ── Primary / unique constraint ───────────────────────────────────────────

    [Fact]
    public void UniqueViolation_PrimaryKeyMessage_Maps_UniqueConstraintViolationException()
    {
        var raw = new SqliteMessageDbException(
            "violation of PRIMARY KEY constraint \"PK_ORDERS\" on table \"ORDERS\"");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Firebird, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void UniqueViolation_UniqueConstraintMessage_Maps_UniqueConstraintViolationException()
    {
        var raw = new SqliteMessageDbException(
            "violation of UNIQUE constraint \"UQ_CUSTOMER_EMAIL\" on table \"CUSTOMERS\"");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    // ── Foreign key constraint ────────────────────────────────────────────────

    [Fact]
    public void ForeignKey_ByMessage_Maps_ForeignKeyViolationException()
    {
        var raw = new SqliteMessageDbException(
            "violation of FOREIGN KEY constraint \"FK_ORDER_ITEM_ORDER\" on table \"ORDER_ITEMS\"");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
        Assert.Equal(SupportedDatabase.Firebird, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── NOT NULL constraint ───────────────────────────────────────────────────

    [Fact]
    public void NotNull_ByValidationError_Maps_NotNullViolationException()
    {
        // Real Firebird NOT NULL violation: "validation error for column X, value \"*** null ***\""
        // This does NOT contain "NOT NULL" — translator must check for "*** null ***"
        var raw = new SqliteMessageDbException(
            "validation error for column \"name\", value \"*** null ***\"");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
        Assert.Equal(SupportedDatabase.Firebird, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void NotNull_ByNotNullKeyword_Maps_NotNullViolationException()
    {
        // Some Firebird error paths include "NOT NULL" explicitly (e.g. named constraints)
        var raw = new SqliteMessageDbException("Column \"name\" is NOT NULL");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
        Assert.Same(raw, result.InnerException);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public void Timeout_TimeoutException_Maps_CommandTimeoutException()
    {
        var raw = new TimeoutException("lock wait timeout exceeded; try restarting transaction");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.True(result.IsTransient);
    }

    // ── Detection order: constraint pattern wins over timeout message ─────────

    [Fact]
    public void UniqueViolation_WithTimeoutKeywordInMessage_ClassifiesAsUniqueNotTimeout()
    {
        // Firebird includes row values in error messages — a key value containing "timeout"
        // (e.g. a lock-resource name like "lock-timeout-{guid}") must not trigger the
        // timeout heuristic. Constraint patterns are checked first.
        var raw = new SqliteMessageDbException(
            "violation of PRIMARY OR UNIQUE constraint; row value was 'lock-timeout-abc123'");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    // ── Check constraint ──────────────────────────────────────────────────────

    [Fact]
    public void CheckConstraint_ByMessage_Maps_CheckConstraintViolationException()
    {
        var raw = new SqliteMessageDbException(
            "Operation violates CHECK constraint CHK_VALUE_TEST_TABLE on table TEST_TABLE");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Firebird, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── Unknown / fallback ────────────────────────────────────────────────────

    [Fact]
    public void Unknown_Maps_DatabaseOperationException()
    {
        var raw = new SqliteMessageDbException("arithmetic exception, numeric overflow, or string truncation");

        var result = _translator.Translate(SupportedDatabase.Firebird, raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
    }
}
