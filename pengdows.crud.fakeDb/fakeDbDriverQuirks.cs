using System.Data.Common;

namespace pengdows.crud.fakeDb;

/// <summary>
/// The ways real ADO.NET drivers depart from the documented System.Data.Common contract, each one a switch. fakeDb is
/// conformant by default (every switch off); turn one on, or start from a driver's preset, to test code against that
/// driver's defect. Set on <see cref="fakeDbFactory.DriverQuirks"/> for every connection the factory creates, or on
/// <see cref="fakeDbConnection.DriverQuirks"/> for one connection.
/// </summary>
/// <remarks>
/// Each switch and preset reproduces what the ADO.NET conformance suite (pengdows.AdoNetApiTest.conformance) recorded
/// against the named driver version on 2026-10-23; the upstream reports and fixes are listed in that repository's
/// docs/UPSTREAM.md. A preset describes the driver version it names, not later releases that fixed the defect.
/// </remarks>
public sealed class fakeDbDriverQuirks
{
    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Prepare</c> returns normally with no connection or a closed one. Microsoft.Data.SqlClient 2.1–6.0 (documented as
    /// its choice), System.Data.SqlClient, ODP.NET 23.8, System.Data.SQLite 1.0.119, DuckDB.NET 1.5.6.1, AseClient 0.19.2.
    /// </summary>
    public bool PrepareWithoutOpenConnection { get; set; }

    /// <summary>Every execute with no connection throws <see cref="NullReferenceException"/>. AseClient 0.19.2.</summary>
    public bool NullReferenceWithoutConnection { get; set; }

    /// <summary>
    /// <c>ExecuteReaderAsync</c> and <c>ExecuteScalarAsync</c> with no connection throw
    /// <see cref="NullReferenceException"/>; the sync methods are conformant. Microsoft.Data.SqlClient 5.2.3, 6.0.2.
    /// </summary>
    public bool AsyncNullReferenceWithoutConnection { get; set; }

    /// <summary>
    /// <c>ExecuteScalar</c> and <c>ExecuteScalarAsync</c> on a closed connection throw
    /// <see cref="NullReferenceException"/>. MySql.Data 8.4.0, 9.3.0, 9.7.0.
    /// </summary>
    public bool ScalarNullReferenceOnClosedConnection { get; set; }

    /// <summary>
    /// <c>CommandText</c> can be changed while the command's reader is open. Microsoft.Data.SqlClient, MySql.Data, ODP.NET,
    /// DuckDB.NET, AseClient.
    /// </summary>
    public bool CommandTextChangeableWithOpenReader { get; set; }

    /// <summary>
    /// <c>Connection</c> can be changed while the command's reader is open. Microsoft.Data.SqlClient, MySql.Data, ODP.NET,
    /// DuckDB.NET, AseClient, Npgsql 5–6.
    /// </summary>
    public bool ConnectionChangeableWithOpenReader { get; set; }

    /// <summary>The command runs again while its reader is open. ODP.NET 23.8, DuckDB.NET, AseClient 0.19.2.</summary>
    public bool ReexecuteWithOpenReaderAllowed { get; set; }

    /// <summary>
    /// What running a command again while its reader is open throws instead of <see cref="InvalidOperationException"/>:
    /// MySql.Data's <c>MySqlException</c>, Npgsql 7–9's <c>NpgsqlOperationInProgressException</c> (the presets supply them).
    /// </summary>
    public Func<Exception>? ReexecuteWithOpenReaderException { get; set; }

    /// <summary>
    /// A command runs under a transaction that belongs to another connection. MySql.Data, Npgsql 4–9, ODP.NET 23.8,
    /// AseClient 0.19.2.
    /// </summary>
    public bool ForeignTransactionAccepted { get; set; }

    /// <summary><c>CommandBehavior.CloseConnection</c> leaves the connection open. AseClient 0.19.2.</summary>
    public bool CloseConnectionIgnored { get; set; }

    /// <summary>
    /// What <c>ExecuteNonQuery</c> returns for a SELECT instead of -1: 0 on DuckDB.NET 1.5.6.1, 1 on AseClient 0.19.2. Null
    /// (the default) is -1.
    /// </summary>
    public int? NonQueryResultForSelect { get; set; }

    /// <summary>A new command's <c>CommandType</c> is 0, not <c>Text</c>. DuckDB.NET 1.5.6.1, AseClient 0.19.2.</summary>
    public bool CommandTypeDefaultsToZero { get; set; }

    /// <summary><c>ExecuteReaderAsync</c> runs although its token is already canceled. MySql.Data 8.4.0.</summary>
    public bool CanceledExecuteReaderAsyncRuns { get; set; }

    /// <summary>
    /// What a parameter value no database type can hold throws instead of <see cref="NotSupportedException"/>, given the
    /// value (null keeps <see cref="NotSupportedException"/> for it): <see cref="ArgumentException"/> on
    /// Microsoft.Data.SqlClient and ODP.NET, <see cref="InvalidCastException"/> on Npgsql and MySql.Data (the presets supply
    /// each driver's).
    /// </summary>
    public Func<object, Exception?>? UnmappableParameterValueException { get; set; }

    // ── Connections ───────────────────────────────────────────────────────────

    /// <summary>An open connection accepts a new <c>ConnectionString</c>. DuckDB.NET 1.0, 1.5.6.1.</summary>
    public bool ConnectionStringSettableWhileOpen { get; set; }

    /// <summary>
    /// What setting <c>ConnectionString</c> on an open connection throws instead of <see cref="InvalidOperationException"/>:
    /// MySql.Data's <c>MySqlException</c> (the preset supplies it).
    /// </summary>
    public Func<Exception>? ConnectionStringWhileOpenException { get; set; }

    // ── Transactions ──────────────────────────────────────────────────────────

    /// <summary>
    /// A committed or rolled back transaction keeps its <c>Connection</c>. Npgsql 5–9, MySql.Data, DuckDB.NET,
    /// AseClient 0.19.2.
    /// </summary>
    public bool CompletedTransactionKeepsConnection { get; set; }

    /// <summary>A disposed transaction keeps its <c>Connection</c>. MySql.Data, DuckDB.NET.</summary>
    public bool DisposedTransactionKeepsConnection { get; set; }

    /// <summary>
    /// Reading a disposed transaction's <c>Connection</c> throws <see cref="ObjectDisposedException"/>. Npgsql 5–9,
    /// System.Data.SQLite 1.0.119, AseClient 0.19.2.
    /// </summary>
    public bool DisposedTransactionThrowsObjectDisposed { get; set; }

    /// <summary>A second <c>Commit</c> or <c>Rollback</c> returns normally. AseClient 0.19.2.</summary>
    public bool TransactionCompletesAgain { get; set; }

    /// <summary>
    /// <c>SupportsSavepoints</c> is false and <c>Save</c>/<c>Rollback(name)</c> throw, but <c>Release</c> returns normally
    /// (.NET's base implementation). System.Data.SqlClient, Microsoft.Data.SqlClient 2.1–4.1, Microsoft.Data.Sqlite
    /// 3.1–5.0, System.Data.SQLite 1.0.119, MySql.Data, DuckDB.NET 1.5.6.1, Npgsql 4, AseClient 0.19.2, IBM.Data.Db2 8.0.
    /// </summary>
    public bool SavepointReleaseIsNoOp { get; set; }

    /// <summary>
    /// <c>SupportsSavepoints</c> is false, yet <c>Save</c>, <c>Rollback(name)</c> and <c>Release</c> run. MySqlConnector
    /// 2.4–2.6 (mysql-net/MySqlConnector#1676), Microsoft.Data.SqlClient 5.2–6.0, Npgsql 5, ODP.NET 23.8.
    /// </summary>
    public bool SavepointsWorkButUnreported { get; set; }

    // ── Parameters ────────────────────────────────────────────────────────────

    /// <summary>
    /// A new parameter's <c>ParameterName</c> is null, not <c>""</c>. MySql.Data, System.Data.SQLite 1.0.119,
    /// AseClient 0.19.2.
    /// </summary>
    public bool ParameterNameDefaultsToNull { get; set; }

    /// <summary>A new parameter's <c>Direction</c> is 0, not <c>Input</c>. DuckDB.NET 1.5.6.1.</summary>
    public bool ParameterDirectionDefaultsToZero { get; set; }

    /// <summary>True when any switch departs from the contract.</summary>
    public bool HasAnyQuirk =>
        PrepareWithoutOpenConnection || NullReferenceWithoutConnection || AsyncNullReferenceWithoutConnection ||
        ScalarNullReferenceOnClosedConnection || CommandTextChangeableWithOpenReader ||
        ConnectionChangeableWithOpenReader || ReexecuteWithOpenReaderAllowed || ReexecuteWithOpenReaderException != null ||
        ForeignTransactionAccepted || CloseConnectionIgnored || NonQueryResultForSelect != null ||
        CommandTypeDefaultsToZero || CanceledExecuteReaderAsyncRuns || UnmappableParameterValueException != null ||
        ConnectionStringSettableWhileOpen || ConnectionStringWhileOpenException != null ||
        CompletedTransactionKeepsConnection || DisposedTransactionKeepsConnection ||
        DisposedTransactionThrowsObjectDisposed || TransactionCompletesAgain || SavepointReleaseIsNoOp ||
        SavepointsWorkButUnreported || ParameterNameDefaultsToNull || ParameterDirectionDefaultsToZero;

    // ── Presets: one driver version each, a new instance per call so a test can change it ──

    /// <summary>Microsoft.Data.SqlClient 6.0.2.</summary>
    public static fakeDbDriverQuirks SqlClient() => new()
    {
        PrepareWithoutOpenConnection = true,
        AsyncNullReferenceWithoutConnection = true,
        CommandTextChangeableWithOpenReader = true,
        ConnectionChangeableWithOpenReader = true,
        SavepointsWorkButUnreported = true,
        // The suite's plain System.Object probe passes on SqlClient, so its handling of that type is not this error (what
        // it does is unrecorded); fakeDb keeps the contract's refusal for it.
        UnmappableParameterValueException = value => value.GetType() == typeof(object)
            ? null
            : new ArgumentException(
                $"No mapping exists from object type {value.GetType().FullName} to a known managed provider native type.")
    };

    /// <summary>Npgsql 9.0.3.</summary>
    public static fakeDbDriverQuirks Npgsql() => new()
    {
        ReexecuteWithOpenReaderException = () => FakeDbConstraintErrors.ProviderError(
            "A command is already in progress."),
        ForeignTransactionAccepted = true,
        CompletedTransactionKeepsConnection = true,
        DisposedTransactionThrowsObjectDisposed = true,
        UnmappableParameterValueException = value => new InvalidCastException(
            $"Writing values of '{value.GetType().FullName}' is not supported for parameters having no NpgsqlDbType or DataTypeName. Try setting one of these values to the expected database type..")
    };

    /// <summary>MySqlConnector 2.6.2.</summary>
    public static fakeDbDriverQuirks MySqlConnector() => new()
    {
        SavepointsWorkButUnreported = true
    };

    /// <summary>MySql.Data 9.7.0 (8.4.0 also runs <c>ExecuteReaderAsync</c> with a canceled token: <see cref="CanceledExecuteReaderAsyncRuns"/>).</summary>
    public static fakeDbDriverQuirks MySqlData() => new()
    {
        ScalarNullReferenceOnClosedConnection = true,
        CommandTextChangeableWithOpenReader = true,
        ConnectionChangeableWithOpenReader = true,
        ReexecuteWithOpenReaderException = () => FakeDbConstraintErrors.ProviderError(
            "There is already an open DataReader associated with this Connection which must be closed first."),
        ForeignTransactionAccepted = true,
        // As for SqlClient, the plain System.Object probe passes on MySql.Data, so the contract's refusal stands for it.
        UnmappableParameterValueException = value => value.GetType() == typeof(object)
            ? null
            : new InvalidCastException("Unable to cast object of type 'System.Byte[]' to type 'System.String'."),
        ConnectionStringWhileOpenException = () => FakeDbConstraintErrors.ProviderError(
            "Not allowed to change the 'ConnectionString' property while the connection (state=Open)."),
        CompletedTransactionKeepsConnection = true,
        DisposedTransactionKeepsConnection = true,
        SavepointReleaseIsNoOp = true,
        ParameterNameDefaultsToNull = true
    };

    /// <summary>DuckDB.NET 1.5.6.1 (most of these are fixed upstream, unreleased).</summary>
    public static fakeDbDriverQuirks DuckDbNet() => new()
    {
        PrepareWithoutOpenConnection = true,
        CommandTextChangeableWithOpenReader = true,
        ConnectionChangeableWithOpenReader = true,
        ReexecuteWithOpenReaderAllowed = true,
        NonQueryResultForSelect = 0,
        CommandTypeDefaultsToZero = true,
        ConnectionStringSettableWhileOpen = true,
        CompletedTransactionKeepsConnection = true,
        DisposedTransactionKeepsConnection = true,
        SavepointReleaseIsNoOp = true,
        ParameterDirectionDefaultsToZero = true
    };

    /// <summary>Oracle.ManagedDataAccess.Core 23.8.</summary>
    public static fakeDbDriverQuirks OracleManagedDataAccess() => new()
    {
        PrepareWithoutOpenConnection = true,
        CommandTextChangeableWithOpenReader = true,
        ConnectionChangeableWithOpenReader = true,
        ReexecuteWithOpenReaderAllowed = true,
        ForeignTransactionAccepted = true,
        SavepointsWorkButUnreported = true,
        UnmappableParameterValueException = _ => new ArgumentException("Value does not fall within the expected range.")
    };

    /// <summary>System.Data.SQLite 1.0.119 (Microsoft.Data.Sqlite 6.0 and later are conformant here).</summary>
    public static fakeDbDriverQuirks SystemDataSqlite() => new()
    {
        PrepareWithoutOpenConnection = true,
        DisposedTransactionThrowsObjectDisposed = true,
        SavepointReleaseIsNoOp = true,
        ParameterNameDefaultsToNull = true
    };

    /// <summary>
    /// AdoNetCore.AseClient 0.19.2. Its <c>AseException</c> is not a <see cref="DbException"/>, which no switch models;
    /// script it with a connection's custom failure exception.
    /// </summary>
    public static fakeDbDriverQuirks AseClient() => new()
    {
        PrepareWithoutOpenConnection = true,
        NullReferenceWithoutConnection = true,
        CommandTextChangeableWithOpenReader = true,
        ConnectionChangeableWithOpenReader = true,
        ReexecuteWithOpenReaderAllowed = true,
        ForeignTransactionAccepted = true,
        CloseConnectionIgnored = true,
        NonQueryResultForSelect = 1,
        CommandTypeDefaultsToZero = true,
        CompletedTransactionKeepsConnection = true,
        DisposedTransactionThrowsObjectDisposed = true,
        TransactionCompletesAgain = true,
        SavepointReleaseIsNoOp = true,
        ParameterNameDefaultsToNull = true
    };

    /// <summary>IBM.Data.Db2 (Net.IBM.Data.Db2-lnx) 8.0.0.500.</summary>
    public static fakeDbDriverQuirks Db2() => new()
    {
        SavepointReleaseIsNoOp = true
    };

    // A new parameter as the driver creates it.
    internal void ApplyTo(DbParameter parameter)
    {
        if (ParameterNameDefaultsToNull)
        {
            parameter.ParameterName = null!;
        }

        if (ParameterDirectionDefaultsToZero)
        {
            parameter.Direction = 0;
        }
    }
}
