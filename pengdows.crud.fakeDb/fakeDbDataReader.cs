#region

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbDataReader : DbDataReader
{
    private readonly List<List<Dictionary<string, object>>> _resultSets;
    private int _currentSetIndex = 0;
    private int _index = -1;

    public fakeDbDataReader(
        IEnumerable<Dictionary<string, object>>? rows = null)
    {
        var single = rows?.ToList() ?? new List<Dictionary<string, object>>();
        _resultSets = new List<List<Dictionary<string, object>>> { single };
    }

    public fakeDbDataReader() : this((IEnumerable<Dictionary<string, object>>?)null)
    {
    }

    /// <summary>
    /// Creates a reader with multiple result sets, allowing <see cref="NextResult"/> to
    /// advance to subsequent sets. Used to simulate compound batch queries
    /// (e.g. INSERT followed by SELECT LAST_INSERT_ID()).
    /// </summary>
    internal fakeDbDataReader(IEnumerable<IEnumerable<Dictionary<string, object>>> resultSets)
    {
        _resultSets = resultSets.Select(rs => rs.ToList()).ToList();
        if (_resultSets.Count == 0)
        {
            _resultSets.Add(new List<Dictionary<string, object>>());
        }
    }

    private List<Dictionary<string, object>> CurrentRows => _resultSets[_currentSetIndex];

    private Dictionary<string, object>? CurrentRow =>
        _index >= 0 && _index < CurrentRows.Count ? CurrentRows[_index] : null;

    /// <summary>Returns the rows in the first result set. Used by RemainingReaderResults.</summary>
    internal List<Dictionary<string, object>> FirstResultSet => _resultSets[0];

    // The current row's column names, cached so reading a value doesn't allocate: the library's
    // allocation tests and benchmarks read through fakeDb. Rows aren't modified while being read.
    private Dictionary<string, object>? _keysRow;
    private string[] _keys = Array.Empty<string>();

    private string[] GetKeys(Dictionary<string, object> row)
    {
        if (!ReferenceEquals(row, _keysRow))
        {
            // Declared columns fix the ordinal order; a row's own key order otherwise.
            _keys = Columns != null ? Columns.Select(c => c.Name).ToArray() : row.Keys.ToArray();
            _keysRow = row;
        }

        return _keys;
    }

    /// <summary>
    /// The result's declared columns. When set, FieldCount, GetName, GetOrdinal, GetFieldType and
    /// GetDataTypeName come from them, also for a result with no rows, as a real provider reports a
    /// result's metadata (e.g. for a "SELECT cols FROM t WHERE 1 = 0" metadata probe).
    /// </summary>
    public IReadOnlyList<fakeDbColumn>? Columns { get; set; }

    public override int FieldCount
        => Columns?.Count
           ?? CurrentRow?.Count
           ?? (CurrentRows.Count > 0 ? CurrentRows[0].Count : 0);

    public override bool HasRows
        => CurrentRows.Count > 0;

    private bool _isClosed;

    // Stubs for unused members
    public override int Depth => 0;

    /// <summary>
    /// Defaults to 0 (ADO.NET's convention for a reader with no applicable affected-row count,
    /// e.g. a SELECT). Some EF Core providers' modification-command-batch implementations read
    /// this directly to determine SaveChanges rows-affected (e.g. Snowflake's
    /// SnowflakeModificationCommandBatch.ConsumeResultSetWithRowsAffectedOnlyAsync reads
    /// reader.DbDataReader.RecordsAffected), rather than reading a row/column value the way
    /// SQLite's provider-generated "SELECT changes()" pattern does.
    /// </summary>
    public int RecordsAffectedOverride { get; set; }

    /// <summary>
    /// When greater than zero, each <see cref="GetBytes"/> call that copies into a buffer returns
    /// at most this many bytes, as streaming providers may — lets a test prove its caller keeps
    /// reading until it has the whole value. Zero (the default) copies everything requested.
    /// </summary>
    public int MaxBytesPerGetBytesCall { get; set; }

    /// <summary>
    /// When true, a <see cref="GetBytes"/> call that copies into a buffer from an offset at or past the
    /// end of the value throws <see cref="IndexOutOfRangeException"/>, as MySql.Data does; MySqlConnector,
    /// Npgsql, SqlClient and Microsoft.Data.Sqlite return 0 (the default here). A length query (null
    /// buffer) never throws.
    /// </summary>
    public bool ThrowsReadingPastEnd { get; set; }

    /// <summary>
    /// When set, accessing <see cref="RecordsAffected"/> throws this exception instead of
    /// returning <see cref="RecordsAffectedOverride"/> — cleared after throwing once. Needed to
    /// simulate a raw provider failure for a provider whose rows-affected check reads
    /// <c>DbDataReader.RecordsAffected</c> directly and never calls <see cref="Read"/>/
    /// <see cref="ReadAsync"/> at all, so <see cref="FailException"/> (which only fires from
    /// those) can never reach it.
    /// </summary>
    public Exception? RecordsAffectedException { get; set; }

    public override int RecordsAffected
    {
        get
        {
            if (RecordsAffectedException != null)
            {
                var ex = RecordsAffectedException;
                RecordsAffectedException = null;
                throw ex;
            }

            return RecordsAffectedOverride;
        }
    }

    public override object this[int i] => GetValue(i);

    public override object this[string name]
    {
        get
        {
            var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null);
            if (row is null)
            {
                throw new IndexOutOfRangeException("No current row.");
            }

            return row[name];
        }
    }

    public override bool IsClosed => _isClosed;

    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        var value = GetValue(ordinal);
        return Task.FromResult((T)Convert.ChangeType(value, typeof(T)));
    }

    /// <summary>
    /// When set together with <see cref="FailException"/>, the read that would advance past this
    /// many successfully-returned rows throws instead — simulating a reader that fails partway
    /// through enumeration (e.g. a dropped connection mid-stream) rather than one that fails to
    /// open at all.
    /// </summary>
    public int? FailAfterReadCount { get; set; }

    /// <summary>See <see cref="FailAfterReadCount"/>. Cleared after throwing once.</summary>
    public Exception? FailException { get; set; }

    /// <summary>
    /// When set together with <see cref="CancelSource"/>, the read that would advance past this
    /// many successfully-returned rows cancels that source first — simulating a caller
    /// cancelling the ambient token mid-stream. Requires the real, ambient
    /// <see cref="CancellationToken"/> passed to <see cref="ReadAsync"/> to actually be
    /// <see cref="CancelSource"/>'s token (or a token derived from it), since only that token's
    /// cancellation is honored — a canned/injected exception like <see cref="FailException"/>
    /// would prove nothing about whether real cancellation-token propagation works.
    /// </summary>
    public int? CancelAfterReadCount { get; set; }

    /// <summary>See <see cref="CancelAfterReadCount"/>.</summary>
    public CancellationTokenSource? CancelSource { get; set; }

    /// <summary>
    /// Emulates <see cref="CommandBehavior.SequentialAccess"/> as SqlClient enforces it: within a row,
    /// reading a column before the last one read throws <see cref="InvalidOperationException"/>;
    /// re-reading the current column (e.g. IsDBNull, then GetString) is allowed. fakeDbCommand sets it
    /// when a reader is executed with SequentialAccess.
    /// </summary>
    public bool EnforceSequentialAccess { get; set; }

    private int _sequentialLastOrdinal = -1;

    private void CheckSequentialAccess(int ordinal)
    {
        if (!EnforceSequentialAccess)
        {
            return;
        }

        if (ordinal < _sequentialLastOrdinal)
        {
            throw new InvalidOperationException(
                $"Invalid attempt to read from column ordinal '{ordinal}'. With CommandBehavior.SequentialAccess, you may only read from column ordinal '{_sequentialLastOrdinal}' or greater.");
        }

        _sequentialLastOrdinal = ordinal;
    }

    public override bool Read()
    {
        _sequentialLastOrdinal = -1;
        if (FailException != null && FailAfterReadCount.HasValue && _index + 1 >= FailAfterReadCount.Value)
        {
            var ex = FailException;
            FailException = null;
            throw ex;
        }

        return ++_index < CurrentRows.Count;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        if (CancelSource != null
            && CancelAfterReadCount.HasValue
            && _index + 1 >= CancelAfterReadCount.Value
            && !CancelSource.IsCancellationRequested)
        {
            CancelSource.Cancel();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        try
        {
            return Task.FromResult(Read());
        }
        catch (Exception ex)
        {
            return Task.FromException<bool>(ex);
        }
    }

    /// <summary>
    /// How many <see cref="GetValue"/> calls threw, so a test can show a provider refusal is learned
    /// once per column instead of thrown and caught on every row.
    /// </summary>
    public int GetValueExceptionCount { get; private set; }

    public override object GetValue(int i)
    {
        CheckSequentialAccess(i);
        try
        {
            return GetValueCore(i);
        }
        catch
        {
            GetValueExceptionCount++;
            throw;
        }
    }

    private object GetValueCore(int i)
    {
        var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null)
            ?? throw new IndexOutOfRangeException("No data rows.");
        var keys = GetKeys(row);
        if (ColumnReadExceptions != null && ColumnReadExceptions.TryGetValue(keys[i], out var failure))
        {
            throw failure;
        }

        if (IsHandlerlessColumn(i))
        {
            throw HandlerlessRead(i, "System.Object");
        }

        if (IsDoubleBeyondDecimal(i) && row[keys[i]] is double)
        {
            throw new InvalidCastException("Specified cast is not valid.");
        }

        if (IsUnknownTypeColumn(i) && row[keys[i]] is byte[])
        {
            throw new InvalidCastException("Reading as 'System.Object' is not supported for fields having DataTypeName '-'");
        }

        if (IsNullableElementArray(i) && row[keys[i]] is Array)
        {
            throw new InvalidCastException(
                "Cannot read a non-nullable collection of elements because the returned array contains nulls. Call GetFieldValue with a nullable collection type instead.");
        }

        if (IsUnloadableUdt(i) && row[keys[i]] is not null && row[keys[i]] is not DBNull)
        {
            throw new FileNotFoundException(
                "Could not load file or assembly 'Microsoft.SqlServer.Types, Version=16.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91'. The system cannot find the file specified.",
                "Microsoft.SqlServer.Types, Version=16.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91");
        }

        if (row[keys[i]] is fakeDbInterval interval)
        {
            return interval.ToDriverTimeSpan();
        }

        if (OutOfRangeReturnsNullColumns != null && OutOfRangeReturnsNullColumns.Contains(keys[i]))
        {
            return null!; // deliberately violates the contract, as the emulated driver does
        }

        if (IsInt64TextColumn(i) && row[keys[i]] is string text)
        {
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                ? whole
                : throw Int64TextOverflow(text);
        }

        if (IsProviderDecimal(i) && row[keys[i]] is decimal providerDecimal)
        {
            return new fakeDbProviderDecimal(providerDecimal);
        }

        return row[keys[i]];
    }

    /// <summary>
    /// Columns that emulate Snowflake.Data 5.x on a scale-0 NUMBER (confirmed live 2026-10-02): the
    /// stored value is the number's text; <see cref="GetFieldType"/> reports <see cref="long"/>;
    /// <see cref="GetValue"/>, <see cref="GetInt64"/> and <c>GetFieldValue</c> return the
    /// <see cref="long"/> or, beyond its range, throw <see cref="OverflowException"/>
    /// ("Use GetString() to handle very large values"); <see cref="GetString"/> returns the text,
    /// <see cref="GetDecimal"/> the value when <see cref="decimal"/> holds it, and
    /// <see cref="IsDBNull"/> works whatever the value.
    /// </summary>
    public ISet<string>? Int64TextColumns { get; set; }

    private bool IsInt64TextColumn(int i) => Int64TextColumns != null && Int64TextColumns.Contains(GetName(i));

    private static OverflowException Int64TextOverflow(string text) =>
        new($"Error converting '{text} to Int64'. Use GetString() to handle very large values");

    /// <summary>
    /// Columns whose reads (<see cref="GetValue"/> and every typed getter built on it) throw the
    /// given exception — emulates a provider that can't convert a stored value, e.g. Npgsql's
    /// <see cref="OverflowException"/>, ODP.NET's <see cref="InvalidCastException"/> or
    /// MySqlConnector's <see cref="FormatException"/> for a NUMERIC above <see cref="decimal.MaxValue"/>.
    /// </summary>
    public IDictionary<string, Exception>? ColumnReadExceptions { get; set; }

    /// <summary>
    /// Columns that emulate Informix.Net.Core on a DECIMAL outside <see cref="decimal"/>'s range
    /// (confirmed live 2026-09-30): <see cref="GetValue"/> returns C# <c>null</c> (not
    /// <see cref="DBNull.Value"/>), <see cref="IsDBNull"/> throws <see cref="OverflowException"/>, and
    /// <see cref="GetDecimal"/> throws <see cref="NullReferenceException"/>.
    /// </summary>
    public ISet<string>? OutOfRangeReturnsNullColumns { get; set; }

    /// <summary>
    /// Columns whose <see cref="GetFieldType"/> returns <c>null</c> — emulates Microsoft.Data.SqlClient
    /// on a CLR user-defined type (hierarchyid, geometry) whose assembly (Microsoft.SqlServer.Types)
    /// isn't loaded (confirmed live 2026-09-30).
    /// </summary>
    public ISet<string>? UnresolvedFieldTypeColumns { get; set; }

    /// <summary>
    /// Columns (name → provider data type name, e.g. <c>master.sys.hierarchyid</c>) that emulate
    /// Microsoft.Data.SqlClient on a CLR user-defined type whose assembly (Microsoft.SqlServer.Types)
    /// isn't loaded, as confirmed live on SQL Server 2025 with SqlClient 6.0.2: <see cref="GetFieldType"/>
    /// returns <c>null</c>, <see cref="GetValue"/> throws <see cref="FileNotFoundException"/> for a
    /// non-NULL value, <see cref="GetDataTypeName"/> returns the given name, and <see cref="GetBytes"/>
    /// and <see cref="IsDBNull"/> still work on the stored bytes.
    /// </summary>
    public IDictionary<string, string>? UnloadableUdtColumns { get; set; }

    /// <summary>
    /// Columns (name → PostgreSQL data type name, e.g. <c>public.geometry</c>) that emulate Npgsql 9 on
    /// a type it has no handler for (PostGIS geometry/geography without NetTopologySuite, pgvector's
    /// vector without its plugin), as confirmed live: <see cref="GetFieldType"/>, <see cref="GetValue"/>
    /// and <c>GetFieldValue</c> throw <see cref="InvalidCastException"/> ("Reading as 'System.Object' is
    /// not supported for fields having DataTypeName '...'"), while <see cref="GetDataTypeName"/>,
    /// <see cref="GetBytes"/> (the binary wire value) and <see cref="IsDBNull"/> work.
    /// </summary>
    public IDictionary<string, string>? HandlerlessColumns { get; set; }

    /// <summary>
    /// Columns holding a <see cref="double"/> beyond <see cref="decimal"/>'s range that emulate ODP.NET
    /// on a NUMBER/FLOAT column (confirmed live, Oracle 23ai): <see cref="GetFieldType"/> reports
    /// <see cref="decimal"/>, <see cref="GetValue"/> and <see cref="GetDecimal"/> throw
    /// <see cref="InvalidCastException"/>, and <see cref="GetDouble"/> returns the value.
    /// </summary>
    public ISet<string>? DoubleBeyondDecimalColumns { get; set; }

    /// <summary>
    /// Columns holding a <see cref="byte"/>[] that emulate FirebirdClient 10 on BINARY/VARBINARY
    /// (confirmed live, Firebird 5): <see cref="GetFieldType"/> reports <see cref="string"/> while
    /// <see cref="GetValue"/> returns the bytes.
    /// </summary>
    public ISet<string>? BinaryReportedAsStringColumns { get; set; }

    /// <summary>
    /// Columns holding a <see cref="byte"/>[] that emulate Npgsql 9 on Spanner's uuid (confirmed live):
    /// <see cref="GetFieldType"/> reports <see cref="object"/> and <see cref="GetDataTypeName"/>
    /// ".&lt;unknown&gt;", <see cref="GetValue"/> throws <see cref="InvalidCastException"/>, and
    /// <see cref="GetBytes"/> returns the binary wire value.
    /// </summary>
    public ISet<string>? UnknownTypeColumns { get; set; }

    /// <summary>
    /// Columns holding a nullable-element array (e.g. <c>long?[]</c>) that emulate Npgsql 9 on Spanner's
    /// arrays (confirmed live): <see cref="GetFieldType"/> reports <see cref="Array"/>, while
    /// <see cref="GetValue"/> and <c>GetFieldValue</c> of that type throw <see cref="InvalidCastException"/>
    /// ("Cannot read a non-nullable collection of elements because the returned array contains nulls")
    /// and <c>GetFieldValue</c> of the nullable-element array returns it.
    /// </summary>
    public ISet<string>? NullableElementArrayColumns { get; set; }

    /// <summary>
    /// Columns holding a <see cref="DateTimeOffset"/> that emulate Snowflake.Data on TIMESTAMP_LTZ /
    /// TIMESTAMP_TZ (confirmed live): <see cref="GetFieldType"/> reports <see cref="DateTime"/>,
    /// <see cref="GetValue"/> returns the DateTimeOffset, and <see cref="GetDateTime"/> returns its local
    /// wall time with no offset (for TIMESTAMP_TZ the real driver throws instead).
    /// </summary>
    public ISet<string>? DateTimeOffsetReportedAsDateTimeColumns { get; set; }

    /// <summary>
    /// Field types reported for columns regardless of the value they hold, as a provider reports its
    /// declared type (e.g. InterBaseSql reports an ARRAY column as <see cref="Array"/> and returns a
    /// non-zero-based <c>Int32[*]</c>).
    /// </summary>
    public IDictionary<string, Type>? ReportedFieldTypes { get; set; }

    /// <summary>
    /// Columns holding a <see cref="decimal"/> that emulate Sap.Data.Hana.Net on DECIMAL/SMALLDECIMAL
    /// (confirmed live): <see cref="GetFieldType"/> reports <see cref="decimal"/>, <see cref="GetValue"/>
    /// returns a provider-specific <see cref="IConvertible"/> wrapper (<see cref="fakeDbProviderDecimal"/>),
    /// so <c>GetFieldValue&lt;decimal&gt;</c> throws <see cref="InvalidCastException"/>, and
    /// <see cref="GetDecimal"/> returns the value.
    /// </summary>
    public ISet<string>? ProviderDecimalColumns { get; set; }

    private bool IsProviderDecimal(int i) => ProviderDecimalColumns != null && ProviderDecimalColumns.Contains(GetName(i));

    private bool IsDateTimeOffsetReportedAsDateTime(int i) =>
        DateTimeOffsetReportedAsDateTimeColumns != null && DateTimeOffsetReportedAsDateTimeColumns.Contains(GetName(i));

    private bool IsUnknownTypeColumn(int i) => UnknownTypeColumns != null && UnknownTypeColumns.Contains(GetName(i));

    private bool IsNullableElementArray(int i) =>
        NullableElementArrayColumns != null && NullableElementArrayColumns.Contains(GetName(i));

    /// <summary>
    /// The stored value itself for a provider-specific type (<see cref="fakeDbInterval"/>); otherwise
    /// <see cref="GetValue"/>, as <see cref="System.Data.Common.DbDataReader"/> does.
    /// </summary>
    public override object GetProviderSpecificValue(int ordinal)
    {
        if (RawValue(ordinal) is not fakeDbInterval interval)
        {
            return GetValue(ordinal);
        }

        CheckSequentialAccess(ordinal);
        return interval;
    }

    public override Type GetProviderSpecificFieldType(int ordinal) =>
        RawValue(ordinal) is fakeDbInterval ? typeof(fakeDbInterval) : GetFieldType(ordinal);

    public override T GetFieldValue<T>(int ordinal)
    {
        CheckSequentialAccess(ordinal);
        if (IsNullableElementArray(ordinal) && RawValue(ordinal) is Array stored && stored.GetType() == typeof(T))
        {
            return (T)(object)stored;
        }

        return base.GetFieldValue<T>(ordinal);
    }

    private bool IsDoubleBeyondDecimal(int i) => DoubleBeyondDecimalColumns != null && DoubleBeyondDecimalColumns.Contains(GetName(i));

    private bool IsBinaryReportedAsString(int i) => BinaryReportedAsStringColumns != null && BinaryReportedAsStringColumns.Contains(GetName(i));

    private bool IsHandlerlessColumn(int ordinal) =>
        HandlerlessColumns != null && HandlerlessColumns.ContainsKey(GetName(ordinal));

    private InvalidCastException HandlerlessRead(int ordinal, string clrType) =>
        new($"Reading as '{clrType}' is not supported for fields having DataTypeName '{HandlerlessColumns![GetName(ordinal)]}'");

    private bool IsUnloadableUdt(int ordinal) =>
        UnloadableUdtColumns != null && UnloadableUdtColumns.ContainsKey(GetName(ordinal));

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override string GetName(int i)
    {
        if (Columns != null)
        {
            return Columns[i].Name;
        }

        var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null)
            ?? throw new IndexOutOfRangeException("No data rows.");
        var keys = GetKeys(row);
        return keys[i];
    }

    public override int GetOrdinal(string name)
    {
        if (Columns != null)
        {
            for (var c = 0; c < Columns.Count; c++)
            {
                if (string.Equals(Columns[c].Name, name, StringComparison.Ordinal))
                {
                    return c;
                }
            }

            throw new IndexOutOfRangeException($"Column '{name}' not found.");
        }

        var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null)
            ?? throw new IndexOutOfRangeException("No data rows.");
        var keys = GetKeys(row);
        var ordinal = Array.IndexOf(keys, name);

        // Matches the documented IDataRecord.GetOrdinal contract and every real ADO.NET provider
        // exercised by testbed's cross-engine GetOrdinal probe (TestProvider.
        // TestGetOrdinalUnknownColumnBehavior): 25 of 30 tested engine/version targets throw
        // exactly IndexOutOfRangeException for an unknown column name; the other 5 throw a
        // different exception type but still throw. None returns a sentinel value silently, which
        // is what this method used to do.
        if (ordinal < 0)
        {
            throw new IndexOutOfRangeException(name);
        }

        return ordinal;
    }

    /// <summary>
    /// How many times <see cref="IsDBNull"/> was called, so a test can show a read path avoids it
    /// (it costs a round of driver work per call on some providers, e.g. Npgsql).
    /// </summary>
    public int IsDBNullCallCount { get; private set; }

    public override bool IsDBNull(int i)
    {
        IsDBNullCallCount++;
        CheckSequentialAccess(i);
        if (OutOfRangeReturnsNullColumns != null && OutOfRangeReturnsNullColumns.Contains(GetName(i)))
        {
            throw new OverflowException("Value was either too large or too small for a Decimal.");
        }

        var value = IsUnloadableUdt(i) || IsInt64TextColumn(i) || IsHandlerlessColumn(i) || IsDoubleBeyondDecimal(i) ||
                    IsUnknownTypeColumn(i) || IsNullableElementArray(i) || RawValue(i) is fakeDbInterval
            ? RawValue(i)
            : GetValue(i);
        return value is null || value == DBNull.Value;
    }

    public override bool NextResult()
    {
        if (_currentSetIndex + 1 < _resultSets.Count)
        {
            _currentSetIndex++;
            _index = -1;
            return true;
        }

        return false;
    }

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        => Task.FromResult(NextResult());

    public override bool GetBoolean(int i)
    {
        CheckSequentialAccess(i);
        return (bool)GetValue(i);
    }

    public override byte GetByte(int i)
    {
        CheckSequentialAccess(i);
        return (byte)GetValue(i);
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        CheckSequentialAccess(ordinal);
        var data = IsUnloadableUdt(ordinal) || IsHandlerlessColumn(ordinal) || IsUnknownTypeColumn(ordinal)
            ? RawValue(ordinal)
            : GetValue(ordinal);
        if (data is not byte[] bytes)
        {
            // If it's not a byte array, return 0 to indicate no bytes copied
            return 0;
        }

        var available = bytes.LongLength - dataOffset;
        if (available <= 0)
        {
            if (ThrowsReadingPastEnd && buffer != null)
            {
                throw new IndexOutOfRangeException("Data index must be a valid index in the field");
            }

            return 0;
        }

        // When buffer is null, return the total available length (standard .NET GetBytes convention).
        if (buffer == null)
        {
            return available;
        }

        var toCopy = (int)Math.Min(length, available);
        if (MaxBytesPerGetBytesCall > 0)
        {
            toCopy = Math.Min(toCopy, MaxBytesPerGetBytesCall);
        }

        if (toCopy > 0)
        {
            Array.Copy(bytes, (int)dataOffset, buffer, bufferOffset, toCopy);
        }

        return toCopy;
    }

    public override char GetChar(int i)
    {
        CheckSequentialAccess(i);
        return (char)GetValue(i);
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        CheckSequentialAccess(ordinal);
        var data = (string)GetValue(ordinal);
        var copyLength = Math.Min(length, data.Length - dataOffset);
        if (buffer != null && copyLength > 0)
        {
            data.CopyTo((int)dataOffset, buffer, bufferOffset, (int)copyLength);
        }

        return copyLength;
    }

    public override string GetDataTypeName(int i)
    {
        if (Columns != null)
        {
            return Columns[i].DataTypeName;
        }

        if (UnloadableUdtColumns != null && UnloadableUdtColumns.TryGetValue(GetName(i), out var udtName))
        {
            return udtName;
        }

        if (HandlerlessColumns != null && HandlerlessColumns.TryGetValue(GetName(i), out var pgTypeName))
        {
            return pgTypeName;
        }

        if (IsUnknownTypeColumn(i))
        {
            return ".<unknown>";
        }

        if (RawValue(i) is fakeDbInterval)
        {
            return "Interval";
        }

        return RawValue(i)?.GetType().Name ?? nameof(DBNull);
    }

    public override DateTime GetDateTime(int i)
    {
        CheckSequentialAccess(i);
        if (IsDateTimeOffsetReportedAsDateTime(i) && RawValue(i) is DateTimeOffset offsetValue)
        {
            return DateTime.SpecifyKind(offsetValue.DateTime, DateTimeKind.Unspecified);
        }

        var value = GetValue(i);
        return value switch
        {
            DateTime dt => dt,
            // Real SQLite drivers parse TEXT datetime columns in GetDateTime() using RoundtripKind.
            // Match that behavior so fake readers backed by string values work the same way.
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };
    }

    public override decimal GetDecimal(int i)
    {
        CheckSequentialAccess(i);
        if (IsProviderDecimal(i) && RawValue(i) is decimal providerDecimal)
        {
            return providerDecimal;
        }

        if (IsInt64TextColumn(i) && RawValue(i) is string text)
        {
            return decimal.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new OverflowException($"Error converting '{text} to Decimal'. Use GetString() to handle very large values");
        }

        return (decimal)GetValue(i);
    }

    public override double GetDouble(int i)
    {
        CheckSequentialAccess(i);
        if (IsDoubleBeyondDecimal(i) && RawValue(i) is double value)
        {
            return value;
        }

        return (double)GetValue(i);
    }

    // A real provider reports the column's declared type whatever the current row holds, and never
    // reports NULL as DBNull. fakeDb has no declared types, so it reports the type of the column's
    // first non-null value in the current result set (object when every value is NULL).
    // Column metadata comes from the stored value, never through GetValue: a real provider's
    // GetFieldType/GetDataTypeName don't fail because a value can't be converted.
    private object? RawValue(int i)
    {
        var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null)
            ?? throw new IndexOutOfRangeException("No data rows.");
        return row[GetKeys(row)[i]];
    }

    public override Type GetFieldType(int ordinal)
    {
        if (Columns != null)
        {
            return Columns[ordinal].FieldType;
        }

        if (IsHandlerlessColumn(ordinal))
        {
            throw HandlerlessRead(ordinal, "System.Object");
        }

        if (ReportedFieldTypes != null && ReportedFieldTypes.TryGetValue(GetName(ordinal), out var reported))
        {
            return reported;
        }

        if (IsDoubleBeyondDecimal(ordinal))
        {
            return typeof(decimal);
        }

        if (IsUnknownTypeColumn(ordinal))
        {
            return typeof(object);
        }

        if (IsDateTimeOffsetReportedAsDateTime(ordinal))
        {
            return typeof(DateTime);
        }

        if (RawValue(ordinal) is fakeDbInterval)
        {
            return typeof(TimeSpan);
        }

        if (IsNullableElementArray(ordinal))
        {
            return typeof(Array);
        }

        if (IsBinaryReportedAsString(ordinal))
        {
            return typeof(string);
        }

        if ((UnresolvedFieldTypeColumns != null && UnresolvedFieldTypeColumns.Contains(GetName(ordinal)))
            || IsUnloadableUdt(ordinal))
        {
            return null!; // deliberately violates the contract, as the emulated driver does
        }

        if (IsInt64TextColumn(ordinal))
        {
            return typeof(long);
        }

        var value = RawValue(ordinal);
        if (value is not null && value is not DBNull)
        {
            return value.GetType();
        }

        var name = GetName(ordinal);
        foreach (var row in CurrentRows)
        {
            if (row.TryGetValue(name, out var candidate) && candidate is not null && candidate is not DBNull)
            {
                return candidate.GetType();
            }
        }

        return typeof(object);
    }

    public override float GetFloat(int i)
    {
        CheckSequentialAccess(i);
        return (float)GetValue(i);
    }

    public override Guid GetGuid(int i)
    {
        CheckSequentialAccess(i);
        return (Guid)GetValue(i);
    }

    public override short GetInt16(int i)
    {
        CheckSequentialAccess(i);
        return (short)GetValue(i);
    }

    public override int GetInt32(int i)
    {
        CheckSequentialAccess(i);
        return (int)GetValue(i);
    }

    public override long GetInt64(int i)
    {
        CheckSequentialAccess(i);
        if (GetInt64RejectedColumns != null && GetInt64RejectedColumns.Contains(GetName(i)))
        {
            throw new InvalidCastException("Specified cast is not valid.");
        }

        return (long)GetValue(i);
    }

    /// <summary>
    /// Columns whose <see cref="GetInt64"/> throws <see cref="InvalidCastException"/> while
    /// <see cref="GetValue"/> still returns the <see cref="long"/> and <see cref="GetFieldType"/>
    /// still reports it — emulates Informix.Net.Core, whose GetInt64 rejects a BIGSERIAL column
    /// (confirmed live 2026-09-29; SERIAL8/INT8/BIGINT are unaffected).
    /// </summary>
    public ISet<string>? GetInt64RejectedColumns { get; set; }

    public override string GetString(int i)
    {
        CheckSequentialAccess(i);
        if (IsInt64TextColumn(i) && RawValue(i) is string text)
        {
            return text;
        }

        return (string)GetValue(i);
    }

    public override DataTable? GetSchemaTable()
    {
        return null;
    }

    // Remaining members can throw or return defaults
    public override IEnumerator GetEnumerator()
    {
        return CurrentRows.GetEnumerator();
    }

    public override void Close()
    {
        _isClosed = true;
    }

    protected override DbDataReader GetDbDataReader(int ordinal)
    {
        // Return a new reader with the nested data - this is rarely used in practice
        // Most databases don't support hierarchical data in GetData()

        // Check if we have a valid row position before trying to access data
        if (_index >= 0 && _index < CurrentRows.Count)
        {
            var nestedValue = GetValue(ordinal);
            if (nestedValue is IEnumerable<Dictionary<string, object>> nestedRows)
            {
                return new fakeDbDataReader(nestedRows);
            }
        }

        // For non-nested data or invalid position, return an empty reader
        return new fakeDbDataReader(new List<Dictionary<string, object>>());
    }
}