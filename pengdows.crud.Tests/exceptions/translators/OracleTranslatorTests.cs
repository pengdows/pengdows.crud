using Microsoft.Extensions.Logging.Abstractions;
using System;
using pengdows.crud.enums;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class OracleTranslatorTests
{
    private readonly OracleExceptionTranslator _translator = new();
    private static ISqlDialect TestDialect(SupportedDatabase database) =>
        SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance);

    // ── Unique constraint ─────────────────────────────────────────────────────

    [Fact]
    public void UniqueViolation_ORA00001_Maps_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(1, "ORA-00001: unique constraint (SYSTEM.SYS_C008590) violated");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── Not-null constraint ───────────────────────────────────────────────────

    [Fact]
    public void NotNull_ORA01400_Maps_NotNullViolationException()
    {
        var raw = new NumberedDbException(1400, "ORA-01400: cannot insert NULL into (\"SYSTEM\".\"TEST_TABLE\".\"NAME\")");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── Foreign key constraint ────────────────────────────────────────────────

    [Fact]
    public void ForeignKey_ORA02291_Maps_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(2291, "ORA-02291: integrity constraint violated - parent key not found");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void ForeignKey_ORA02292_Maps_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(2292, "ORA-02292: integrity constraint violated - child record found");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    // ── Check constraint ──────────────────────────────────────────────────────

    [Fact]
    public void Check_ORA02290_Maps_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(2290, "ORA-02290: check constraint (SYSTEM.CHK_VALUE_POSITIVE) violated");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── Deadlock ──────────────────────────────────────────────────────────────

    [Fact]
    public void Deadlock_ORA00060_Maps_DeadlockException()
    {
        var raw = new NumberedDbException(60, "ORA-00060: deadlock detected while waiting for resource");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
        Assert.True(result.IsTransient);
    }

    // ── Serialization conflict ────────────────────────────────────────────────

    [Fact]
    public void SerializationConflict_ORA08177_Maps_SerializationConflictException()
    {
        var raw = new NumberedDbException(8177, "ORA-08177: can't serialize access for this transaction");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public void SnapshotOlderThanDdl_ORA01466_Maps_SerializationConflictException()
    {
        var raw = new NumberedDbException(1466, "ORA-01466: unable to read data - table definition has changed");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Query);

        Assert.IsType<SerializationConflictException>(result);
        Assert.True(result.IsTransient);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public void Timeout_TimeoutException_Maps_CommandTimeoutException()
    {
        var raw = new TimeoutException("query timed out");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.True(result.IsTransient);
    }

    // ── Unknown ───────────────────────────────────────────────────────────────

    [Fact]
    public void Unknown_Maps_DatabaseOperationException()
    {
        var raw = new NumberedDbException(4031, "ORA-04031: unable to allocate shared memory");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
    }

    // ── Detection order: ORA code wins over timeout message ───────────────────

    [Fact]
    public void OraCode1_WithTimeoutKeywordInMessage_ClassifiesAsUniqueNotTimeout()
    {
        // Oracle 23c includes column values in extended error messages; a row value that
        // contains "timeout" must not be misclassified as CommandTimeoutException.
        var raw = new NumberedDbException(1, "ORA-00001: unique constraint (SYS.UK_JOBS) violated; row value was 'timeout'");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    // ── Connection failure ────────────────────────────────────────────────────

    [Fact]
    public void ConnectionFailure_ORA50201_Maps_ConnectionException()
    {
        // Regression: confirmed against a live ODP.NET connect attempt to a closed TCP port —
        // OracleException.Number == 50201, message "ORA-50201: Oracle Communication: Failed to
        // connect to server or failed to parse connect string".
        var raw = new NumberedDbException(50201,
            "ORA-50201: Oracle Communication: Failed to connect to server or failed to parse connect string");

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    // Confirmed live 2026-10-04 (Oracle Free 23ai, ODP.NET, full integration run): with the server's
    // process slots in use, the connect fails as ORA-50201 wrapping a network error whose message is
    // "ORA-12516: Cannot connect to database. Listener ... does not have a protocol handler ... ready".
    // ORA-12516/12519/12520 are the listener's no-free-handler errors: the server is at capacity, not
    // unreachable, so it is a TooManyConnectionsException (transient). A bare 50201 stays a plain
    // connection failure (above).
    [Theory]
    [InlineData("ORA-12516: Cannot connect to database. Listener at host localhost/127.0.0.1 port 32969 does not have a protocol handler for TCP ready or registered for service FREEPDB1.")]
    [InlineData("ORA-12519: TNS:no appropriate service handler found")]
    [InlineData("ORA-12520: TNS:listener could not find available handler for requested type of server")]
    public void ConnectionFailure_ORA50201_WrappingListenerAtCapacity_Maps_TooManyConnections(string inner)
    {
        var raw = new NumberedDbException(50201,
            "ORA-50201: Oracle Communication: Failed to connect to server or failed to parse connect string",
            new InvalidOperationException("ORA-50201: Oracle Communication: Failed to connect to server or failed to parse connect string",
                new InvalidOperationException(inner)));

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Query);

        Assert.IsType<TooManyConnectionsException>(result);
        Assert.True(result.IsTransient);
    }

    // Per-user session limit (confirmed live 2026-09-27, gvenzl/oracle-free with a profile
    // SESSIONS_PER_USER 3, ODP.NET 23.8.0): OracleException.Number 2391. The server-wide
    // 'processes' limit surfaces as ORA-50201 wrapping ORA-12537, which is indistinguishable from any other failed connect, so it stays a plain ConnectionException (covered above). ORA-00018
    // (maximum number of sessions exceeded) and ORA-00020 (maximum number of processes exceeded)
    // are Oracle's documented codes for the other server-wide limits; not reproduced live.
    [Theory]
    [InlineData(2391, "ORA-02391: exceeded simultaneous SESSIONS_PER_USER limit")]
    [InlineData(18, "ORA-00018: maximum number of sessions exceeded")]
    [InlineData(20, "ORA-00020: maximum number of processes (100) exceeded")]
    public void ConnectionLimit_MapsTo_TooManyConnectionsException(int number, string message)
    {
        var raw = new NumberedDbException(number, message);

        var result = _translator.Translate(TestDialect(SupportedDatabase.Oracle), raw, DbOperationKind.Query);

        Assert.IsType<TooManyConnectionsException>(result);
        Assert.True(result.IsTransient);
    }
}
