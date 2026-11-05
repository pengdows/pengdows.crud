#region

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// In-memory data store for FakeDb that provides automatic data persistence
/// Simulates database behavior by storing INSERT data and returning it on SELECT
/// </summary>
/// <remarks>
/// This is a regex/prefix-based SQL parser, not a real SQL engine — it recognizes only the
/// handful of statement shapes pengdows.crud itself generates. A passing test against
/// <see cref="FakeDataStore"/> proves that SQL was shaped the way this parser expects; it is
/// NOT evidence that a real database would accept or execute that SQL correctly. Use
/// <c>testbed</c>/integration tests against real database containers for correctness evidence.
/// </remarks>
public class FakeDataStore
{
    // Table_name -> List of rows (each row is a Dictionary of column->value)
    private readonly ConcurrentDictionary<string, List<Dictionary<string, object?>>> _tables = new();
    private readonly object _lockObject = new();
    private int _nextId = 1;
    private int _lastInsertId = 0;

    /// <summary>
    /// When <c>false</c> (the default, unchanged from before this flag existed), a statement
    /// shape this parser doesn't recognize silently "succeeds" instead of failing loudly: an
    /// unparseable INSERT and any other unrecognized nonquery SQL both return 1 affected row, and
    /// an unrecognized SELECT returns zero rows. When <c>true</c>, all three throw
    /// <see cref="NotSupportedException"/> instead — for a test that wants the stronger
    /// substitutability guarantee that every statement it runs through fakeDb was actually
    /// understood, not just tolerated.
    /// </summary>
    public bool StrictMode { get; set; }

    private readonly ConcurrentDictionary<string, string[]> _uniqueKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The product whose provider-shaped violation a duplicate key raises (set by <see cref="fakeDbFactory"/> from the product
    /// it emulates). <see cref="SupportedDatabase.Unknown"/> raises a SQLSTATE 23505 error.
    /// </summary>
    public SupportedDatabase ErrorShape { get; set; }

    /// <summary>
    /// Declares a unique key on <paramref name="table"/>. An INSERT whose key columns equal an existing row's raises the
    /// emulated product's unique violation; <c>INSERT ... ON CONFLICT</c> and <c>ON DUPLICATE KEY UPDATE</c> update the
    /// existing row instead, atomically. This is a model, not an engine: <c>MERGE</c> is not modelled, so the
    /// MERGE-versus-concurrent-insert race still needs a real database.
    /// </summary>
    public void DefineUniqueKey(string table, params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Length == 0)
        {
            throw new ArgumentException("A unique key needs at least one column.", nameof(columns));
        }

        _uniqueKeys[CleanIdentifier(table)] = columns.Select(CleanIdentifier).ToArray();
    }

    private static object? ColumnValue(Dictionary<string, object?> row, string column)
    {
        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static bool SameKey(Dictionary<string, object?> a, Dictionary<string, object?> b, string[] key)
    {
        foreach (var column in key)
        {
            var left = ColumnValue(a, column);
            var right = ColumnValue(b, column);
            if (left == null || right == null)
            {
                return false;
            }

            if (!string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture),
                    Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public void Clear()
    {
        lock (_lockObject)
        {
            _tables.Clear();
            _nextId = 1;
            _lastInsertId = 0;
        }
    }

    public int ExecuteNonQuery(string commandText, DbParameterCollection? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return 0;
        }

        ThrowIfNotSql(commandText);

        // A statement that returns rows changes none: ExecuteNonQuery reports -1 for it (DbCommand.ExecuteNonQuery).
        if (FakeConstantSelect.TryEvaluate(commandText, parameters, out _) ||
            FakeConstantSelect.StartsWithKeyword(commandText, "SELECT"))
        {
            return -1;
        }

        var sql = commandText.Trim().ToUpperInvariant();

        // Handle CREATE TABLE
        if (sql.StartsWith("CREATE TABLE"))
        {
            return HandleCreateTable(commandText);
        }

        // Handle INSERT
        if (sql.StartsWith("INSERT"))
        {
            return HandleInsert(commandText, parameters);
        }

        // Handle UPDATE
        if (sql.StartsWith("UPDATE"))
        {
            return HandleUpdate(commandText, parameters);
        }

        // Handle DELETE
        if (sql.StartsWith("DELETE"))
        {
            return HandleDelete(commandText, parameters);
        }

        // Default for other operations
        if (StrictMode)
        {
            throw new NotSupportedException(
                $"FakeDataStore (StrictMode) does not recognize this nonquery statement shape: {commandText}");
        }

        return 1;
    }

    public object? ExecuteScalar(string commandText, DbParameterCollection? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        ThrowIfNotSql(commandText);

        // The first column of the first row of the first result, or null when it has no rows (DbCommand.ExecuteScalar).
        if (FakeConstantSelect.TryEvaluate(commandText, parameters, out var constantResults))
        {
            // A NULL cell is DBNull (null is "no row"): the command tells the two apart.
            return constantResults[0].Count == 0 ? null : constantResults[0][0].Values.FirstOrDefault() ?? DBNull.Value;
        }

        var sql = commandText.Trim().ToUpperInvariant();

        // Handle SELECT 1 and similar simple queries
        if (sql == "SELECT 1" || sql == "SELECT 1;" || sql.StartsWith("SELECT 1 "))
        {
            return 1;
        }

        // Handle SQLite last_insert_rowid() function
        if (sql == "SELECT LAST_INSERT_ROWID()" || sql == "SELECT LAST_INSERT_ROWID();")
        {
            return _lastInsertId;
        }

        // Handle COUNT queries
        if (sql.Contains("COUNT("))
        {
            return HandleCountQuery(commandText, parameters);
        }

        // Handle simple SELECT queries that should return scalar values
        if (sql.StartsWith("SELECT"))
        {
            var results = ExecuteReader(commandText, parameters);
            if (results.Any())
            {
                var firstRow = results.First();
                return firstRow.Values.FirstOrDefault() ?? DBNull.Value;
            }
        }

        return null;
    }

    public int LastInsertId => _lastInsertId;

    /// <summary>
    /// Every result set the command produces: one per statement for a batch of constant SELECTs, otherwise the single
    /// result <see cref="ExecuteReader"/> returns.
    /// </summary>
    // The UDT a server reports for a column it stores a spatial value in, by table and column (SQL Server's geometry or
    // geography), and the map the last SELECT read with (the reader reports those columns as unloadable UDTs).
    private readonly Dictionary<string, Dictionary<string, string>> _udtColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _spatialUdtIndexes = new();

    /// <summary>
    /// The result sets of a read, and the UDT names of the columns its SELECT read (null when it read none). The map is
    /// returned with the result, so no other read on the same store can change it.
    /// </summary>
    internal List<List<Dictionary<string, object?>>> ExecuteReaderResultSets(string commandText,
        DbParameterCollection? parameters, out IReadOnlyDictionary<string, string>? udtColumns)
    {
        udtColumns = null;
        if (!string.IsNullOrWhiteSpace(commandText))
        {
            ThrowIfNotSql(commandText);
            if (FakeConstantSelect.TryEvaluate(commandText, parameters, out var resultSets))
            {
                return resultSets;
            }
        }

        var rows = ExecuteReader(commandText, parameters).ToList();
        udtColumns = UdtColumnsOf(commandText);
        return [rows];
    }

    private IReadOnlyDictionary<string, string>? UdtColumnsOf(string commandText)
    {
        var from = Regex.Match(commandText, @"\bFROM\s+([`\[\]""'.\w]+)", RegexOptions.IgnoreCase);
        if (!from.Success)
        {
            return null;
        }

        lock (_lockObject)
        {
            return _udtColumns.TryGetValue(CleanIdentifier(from.Groups[1].Value), out var columns)
                ? new Dictionary<string, string>(columns, StringComparer.OrdinalIgnoreCase)
                : null;
        }
    }

    // In StrictMode, text that is not a statement at all fails as the database would: with the emulated product's syntax
    // error, a DbException. SQL the store does not model still throws NotSupportedException.
    private void ThrowIfNotSql(string commandText)
    {
        if (StrictMode && FakeConstantSelect.IsNotSql(commandText))
        {
            throw FakeDbConstraintErrors.SyntaxError(ErrorShape, FakeConstantSelect.FirstKeyword(commandText)!);
        }
    }

    public IEnumerable<Dictionary<string, object?>> ExecuteReader(string commandText,
        DbParameterCollection? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return Enumerable.Empty<Dictionary<string, object?>>();
        }

        ThrowIfNotSql(commandText);

        var sql = commandText.Trim().ToUpperInvariant();

        // Handle last-insert-id functions (SQLite, MySQL, etc.)
        if (sql.Contains("LAST_INSERT_ROWID") || sql.Contains("LAST_INSERT_ID") ||
            sql.Contains("SCOPE_IDENTITY") || sql.Contains("LASTVAL") ||
            sql.Contains("@@IDENTITY"))
        {
            return [new Dictionary<string, object?> { ["id"] = (object?)_lastInsertId }];
        }

        // Handle SELECT queries
        if (sql.StartsWith("SELECT"))
        {
            return HandleSelect(commandText, parameters);
        }

        // Handle INSERT ... RETURNING: execute the INSERT and return the generated ID
        if (sql.StartsWith("INSERT") && sql.Contains("RETURNING"))
        {
            HandleInsert(commandText, parameters);
            return [new Dictionary<string, object?> { ["id"] = (object?)_lastInsertId }];
        }

        return Enumerable.Empty<Dictionary<string, object?>>();
    }

    private int HandleCreateTable(string commandText)
    {
        // Extract table name from CREATE TABLE statement
        var match = Regex.Match(commandText, @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([`\[\]""'\w]+)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var tableName = CleanIdentifier(match.Groups[1].Value);
            lock (_lockObject)
            {
                if (!_tables.ContainsKey(tableName))
                {
                    _tables[tableName] = new List<Dictionary<string, object?>>();
                }
            }

            return 1; // CREATE TABLE executed successfully
        }

        return 0; // Failed to parse CREATE TABLE
    }

    private int HandleInsert(string commandText, DbParameterCollection? parameters)
    {
        commandText = RewriteInsertSelectAsValues(commandText);

        // Match INSERT INTO table (columns) portion — handles both single and multi-row VALUES
        var headerMatch = Regex.Match(commandText,
            @"INSERT\s+INTO\s+([`\[\]""'.\w]+)\s*\(([^)]+)\)\s*VALUES\s*",
            RegexOptions.IgnoreCase);

        if (!headerMatch.Success)
        {
            // Try columnar INSERT INTO table VALUES format (no explicit column list)
            var simpleMatch = Regex.Match(commandText,
                @"INSERT\s+INTO\s+([`\[\]""'.\w]+)\s+VALUES\s*\(([^)]+)\)",
                RegexOptions.IgnoreCase);
            if (simpleMatch.Success)
            {
                var tableName = CleanIdentifier(simpleMatch.Groups[1].Value);
                EnsureTable(tableName);

                var nextId = _nextId++;
                _lastInsertId = nextId;
                var row = new Dictionary<string, object?>
                {
                    ["Id"] = nextId,
                    ["Data"] = simpleMatch.Groups[2].Value.Trim()
                };
                lock (_lockObject)
                {
                    _tables[tableName].Add(row);
                }
                return 1;
            }

            if (StrictMode)
            {
                throw new NotSupportedException(
                    $"FakeDataStore (StrictMode) does not recognize this INSERT statement shape: {commandText}");
            }

            return 1; // Unrecognized INSERT format — treat as success
        }

        var table = CleanIdentifier(headerMatch.Groups[1].Value);
        var columns = headerMatch.Groups[2].Value
            .Split(',')
            .Select(c => CleanIdentifier(c.Trim()))
            .ToList();

        EnsureTable(table);

        // Extract ALL value-row groups after the VALUES keyword.
        // Multi-row batch: VALUES (...), (...), (...)
        // Truncate before any trailing clause so their parentheses aren't misread as value rows:
        //   ON CONFLICT (col) DO UPDATE ...   (SQLite upsert)
        //   RETURNING id                       (PostgreSQL identity)
        //   OUTPUT INSERTED.id                 (SQL Server identity)
        //   ON DUPLICATE KEY UPDATE ...        (MySQL upsert)
        var afterValues = commandText.Substring(headerMatch.Index + headerMatch.Length);
        var trailingClause = Regex.Match(afterValues,
            @"\s+(ON\s+CONFLICT|ON\s+DUPLICATE\s+KEY|RETURNING|OUTPUT)\b",
            RegexOptions.IgnoreCase);
        var upsert = trailingClause.Success &&
                     trailingClause.Groups[1].Value.StartsWith("ON", StringComparison.OrdinalIgnoreCase);
        if (trailingClause.Success)
        {
            afterValues = afterValues.Substring(0, trailingClause.Index);
        }

        var valueRowMatches = ExtractTuples(afterValues);

        var rowsInserted = 0;
        var positional = 0;
        foreach (var tuple in valueRowMatches)
        {
            var values = ParseValues(tuple, parameters, ref positional);
            foreach (var (index, udt) in _spatialUdtIndexes)
            {
                if (index < columns.Count)
                {
                    lock (_lockObject)
                    {
                        if (!_udtColumns.TryGetValue(table, out var udtColumns))
                        {
                            udtColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            _udtColumns[table] = udtColumns;
                        }

                        udtColumns[CleanIdentifier(columns[index])] = udt;
                    }
                }
            }

            var rowData = new Dictionary<string, object?>();

            // Auto-assign ID when the table has no explicit Id column
            if (!columns.Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase)))
            {
                var nextId = _nextId++;
                _lastInsertId = nextId;
                rowData["Id"] = nextId;
            }

            for (var i = 0; i < Math.Min(columns.Count, values.Count); i++)
            {
                rowData[columns[i]] = values[i];

                // Track last inserted ID for any numeric Id column
                if (columns[i].Equals("Id", StringComparison.OrdinalIgnoreCase) && values[i] != null)
                {
                    try
                    {
                        _lastInsertId = Convert.ToInt32(values[i]);
                    }
                    catch (FormatException)
                    {
                        // Best-effort ID tracking; safe to ignore for non-numeric primary keys
                    }
                    catch (InvalidCastException)
                    {
                        // Best-effort ID tracking; safe to ignore for non-numeric primary keys
                    }
                    catch (OverflowException)
                    {
                        // Best-effort ID tracking; safe to ignore for non-numeric primary keys
                    }
                }
            }

            lock (_lockObject)
            {
                var rows = _tables[table];
                if (_uniqueKeys.TryGetValue(table, out var key) &&
                    rows.Find(existing => SameKey(existing, rowData, key)) is { } duplicate)
                {
                    if (!upsert)
                    {
                        throw FakeDbConstraintErrors.UniqueViolation(ErrorShape, table, table + "_" + string.Join("_", key));
                    }

                    foreach (var pair in rowData)
                    {
                        duplicate[pair.Key] = pair.Value;
                    }
                }
                else
                {
                    rows.Add(rowData);
                }
            }
            rowsInserted++;
        }

        return rowsInserted > 0 ? rowsInserted : 1;
    }

    private int HandleUpdate(string commandText, DbParameterCollection? parameters)
    {
        // Simple UPDATE parsing
        var updateMatch = Regex.Match(commandText,
            @"UPDATE\s+([`\[\]""'.\w]+)\s+SET\s+(.+?)(?:\s+WHERE\s+(.+))?$",
            RegexOptions.IgnoreCase);

        if (!updateMatch.Success)
        {
            if (StrictMode)
            {
                throw new NotSupportedException(
                    $"FakeDataStore (StrictMode) does not recognize this UPDATE statement shape: {commandText}");
            }

            // Unparsed UPDATE: 0 rows keeps the lenient default, but pengdows reads 0 as a lost
            // optimistic-lock race, so strict mode refuses the shape instead.
            return 0;
        }

        var tableName = CleanIdentifier(updateMatch.Groups[1].Value);
        var setPart = updateMatch.Groups[2].Value;
        var wherePart = updateMatch.Groups.Count > 3 ? updateMatch.Groups[3].Value : null;

        EnsureTable(tableName);

        lock (_lockObject)
        {
            var rows = _tables[tableName];
            var updatedCount = 0;

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(wherePart) || EvaluateWhereClause(row, wherePart, parameters))
                {
                    ApplySetClause(row, setPart, parameters);
                    updatedCount++;
                }
            }

            return updatedCount;
        }
    }

    private int HandleDelete(string commandText, DbParameterCollection? parameters)
    {
        // Simple DELETE parsing
        var deleteMatch = Regex.Match(commandText,
            @"DELETE\s+FROM\s+([`\[\]""'.\w]+)(?:\s+WHERE\s+(.+))?$",
            RegexOptions.IgnoreCase);

        if (!deleteMatch.Success)
        {
            if (StrictMode)
            {
                throw new NotSupportedException(
                    $"FakeDataStore (StrictMode) does not recognize this DELETE statement shape: {commandText}");
            }

            return 0;
        }

        var tableName = CleanIdentifier(deleteMatch.Groups[1].Value);
        var wherePart = deleteMatch.Groups.Count > 2 ? deleteMatch.Groups[2].Value : null;

        EnsureTable(tableName);

        lock (_lockObject)
        {
            var rows = _tables[tableName];
            var toRemove = new List<Dictionary<string, object?>>();

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(wherePart) || EvaluateWhereClause(row, wherePart, parameters))
                {
                    toRemove.Add(row);
                }
            }

            foreach (var row in toRemove)
            {
                rows.Remove(row);
            }

            return toRemove.Count;
        }
    }

    // Positional (bare ?) markers in a WHERE clause bind in statement order: _whereMarkerBase counts the
    // markers before the WHERE, and _whereMarkerCursor counts the ones already evaluated for the current row.
    private int _whereMarkerBase;
    private int _whereMarkerCursor;

    // A parameter marker in any product's style: @name, ?name, :name (Oracle), $name (DuckDB), or a bare ?.
    private static readonly Regex WholeMarker = new(@"^(?:@\w+|:\w+|\$\w+|\?)$", RegexOptions.Compiled);

    private static bool IsParameterMarker(string text) => WholeMarker.IsMatch(text);

    private static string BareParameterName(string name) => name.TrimStart('@', '?', ':', '$');

    private IEnumerable<Dictionary<string, object?>> HandleSelect(string commandText, DbParameterCollection? parameters)
    {
        // Handle COUNT queries via reader path (ExecuteScalarCore uses ExecuteReader, not ExecuteScalar)
        if (commandText.Contains("COUNT(", StringComparison.OrdinalIgnoreCase))
        {
            var countValue = HandleCountQuery(commandText, parameters) ?? 0L;
            return [new Dictionary<string, object?> { ["count"] = countValue }];
        }

        // Handle literal SELECT without FROM (e.g., SELECT 1 as id, 'test' as name)
        var literalMatch = Regex.Match(commandText,
            @"^SELECT\s+(.+?)(?:\s+FROM\s|$)", RegexOptions.IgnoreCase);

        if (literalMatch.Success && !commandText.Contains("FROM", StringComparison.OrdinalIgnoreCase))
        {
            return HandleLiteralSelect(literalMatch.Groups[1].Value.Trim(), parameters);
        }

        // Handle SELECT * FROM table [alias] [WHERE ...] [ORDER BY ...]. The FROM is the statement's own: a scalar subquery
        // in the select list (Oracle's SDO_GEOMETRY read, FROM DUAL) has FROMs inside its parentheses.
        var parts = SplitSelect(commandText);

        if (parts is null)
        {
            if (StrictMode)
            {
                throw new NotSupportedException(
                    $"FakeDataStore (StrictMode) does not recognize this SELECT statement shape: {commandText}");
            }

            return Enumerable.Empty<Dictionary<string, object?>>();
        }

        var selectPart = parts.Value.SelectList;
        var tableName = CleanIdentifier(parts.Value.Table);
        var wherePart = parts.Value.Where;

        EnsureTable(tableName);

        lock (_lockObject)
        {
            var rows = _tables[tableName];
            var filteredRows = rows.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(wherePart))
            {
                var whereIndex = commandText.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
                _whereMarkerBase = whereIndex < 0 ? 0 : commandText[..whereIndex].Count(c => c == '?');
                filteredRows = filteredRows.Where(row =>
                {
                    _whereMarkerCursor = 0;
                    return EvaluateWhereClause(row, wherePart, parameters);
                });
            }

            // Handle column selection
            if (selectPart == "*")
            {
                return filteredRows.ToList();
            }

            // Handle specific columns, honoring an optional "AS alias" or implicit "column alias"
            // suffix on each — mirrors HandleLiteralSelect's identical alias pattern for the
            // no-FROM literal-select path, so both SELECT forms support aliasing consistently.
            var columnSpecs = SplitTopLevel(selectPart, ',').Select(ParseColumnSpec).ToList();
            return filteredRows.Select(row =>
            {
                var result = new Dictionary<string, object?>();
                foreach (var (sourceColumn, outputKey) in columnSpecs)
                {
                    if (row.ContainsKey(sourceColumn))
                    {
                        result[outputKey] = row[sourceColumn];
                    }
                }

                return result;
            }).ToList();
        }
    }

    // The SELECT's select list, table and WHERE clause, split at the first FROM that is not inside parentheses or quotes.
    private static SelectParts? SplitSelect(string commandText)
    {
        var select = Regex.Match(commandText, @"^\s*SELECT\s+", RegexOptions.IgnoreCase);
        if (!select.Success)
        {
            return null;
        }

        var from = TopLevelFrom(commandText, select.Length);
        if (from < 0)
        {
            return null;
        }

        var tail = Regex.Match(commandText[from..],
            @"^FROM\s+([`\[\]""'.\w]+)(?:\s+(?:AS\s+)?[`\[\]""'\w]+)?(?:\s+WHERE\s+([\s\S]+?))?(?:\s+ORDER\s+BY\s+.+)?$",
            RegexOptions.IgnoreCase);
        if (!tail.Success)
        {
            return null;
        }

        return new SelectParts(commandText[select.Length..from].Trim(), tail.Groups[1].Value,
            tail.Groups[2].Success ? tail.Groups[2].Value : null);
    }

    private readonly record struct SelectParts(string SelectList, string Table, string? Where);

    private static int TopLevelFrom(string text, int start)
    {
        var depth = 0;
        char? quote = null;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } q)
            {
                if (c == q)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (depth == 0 && (i == 0 || char.IsWhiteSpace(text[i - 1])) &&
                     string.Compare(text, i, "FROM", 0, 4, StringComparison.OrdinalIgnoreCase) == 0 &&
                     (i + 4 == text.Length || char.IsWhiteSpace(text[i + 4])))
            {
                return i;
            }
        }

        return -1;
    }

    private (string SourceColumn, string OutputKey) ParseColumnSpec(string token)
    {
        var trimmed = token.Trim();
        var match = Regex.Match(trimmed, @"^(.+?)\s+(?:AS\s+)?""?([a-zA-Z_][a-zA-Z0-9_]*)""?$", RegexOptions.IgnoreCase);

        // Oracle's spatial read: "SRID=n;" then SDO_UTIL.TO_WKTGEOMETRY(col) over a CASE. The column's stored EWKT is that text.
        var spatial = Regex.Match(trimmed, @"SDO_UTIL\.TO_WKTGEOMETRY\(\s*([^)]+?)\s*\)", RegexOptions.IgnoreCase);
        if (spatial.Success && match.Success)
        {
            // The column may be qualified ("alias"."column"): the column is the last name.
            var reference = spatial.Groups[1].Value.Split('.').Last();
            return (CleanIdentifier(reference), match.Groups[2].Value.Trim());
        }

        if (match.Success)
        {
            var sourceColumn = CleanIdentifier(ColumnOfExpression(StripConversionCall(match.Groups[1].Value.Trim())));
            var alias = match.Groups[2].Value.Trim();
            return (sourceColumn, alias);
        }

        var column = CleanIdentifier(trimmed);
        return (column, column);
    }

    private object? HandleCountQuery(string commandText, DbParameterCollection? parameters)
    {
        // Extract table name from COUNT query
        var countMatch = Regex.Match(commandText,
            @"SELECT\s+COUNT\([^)]*\)\s+FROM\s+([`\[\]""'.\w]+)(?:\s+WHERE\s+(.+))?$",
            RegexOptions.IgnoreCase);

        if (!countMatch.Success)
        {
            return 0;
        }

        var tableName = CleanIdentifier(countMatch.Groups[1].Value);
        var wherePart = countMatch.Groups.Count > 2 ? countMatch.Groups[2].Value : null;

        EnsureTable(tableName);

        lock (_lockObject)
        {
            var rows = _tables[tableName];

            if (string.IsNullOrWhiteSpace(wherePart))
            {
                return (long)rows.Count;
            }

            return (long)rows.Count(row => EvaluateWhereClause(row, wherePart, parameters));
        }
    }

    private void EnsureTable(string tableName)
    {
        lock (_lockObject)
        {
            if (!_tables.ContainsKey(tableName))
            {
                _tables[tableName] = new List<Dictionary<string, object?>>();
            }
        }
    }

    private string CleanIdentifier(string identifier)
    {
        var cleaned = identifier.Trim().Trim('`', '[', ']', '"', '\'');

        var lastDot = cleaned.LastIndexOf('.');
        if (lastDot >= 0)
        {
            cleaned = cleaned.Substring(lastDot + 1).Trim('`', '[', ']', '"', '\'');
        }

        return cleaned;
    }

    // The product this store emulates; set by the connection that owns it. Unknown until a product is named.
    internal SupportedDatabase Product { get; set; } = SupportedDatabase.Unknown;

    // Fractional-second precision a product's column keeps (docs/type-support-matrix.md). The extra digits are
    // truncated, never rounded. Sybase ASE's BIGDATETIME keeps microseconds; SingleStore's TIME(6) and Oracle's
    // INTERVAL DAY TO SECOND(6) keep microseconds for time values. Other products keep the full tick.
    private object? TruncateToStoredPrecision(object? value)
    {
        const long MicrosecondTicks = 10;
        switch (Product, value)
        {
            case (SupportedDatabase.SybaseASE, DateTime dt):
                return new DateTime(dt.Ticks - dt.Ticks % MicrosecondTicks, dt.Kind);
            case (SupportedDatabase.SybaseASE, DateTimeOffset dto):
                return new DateTimeOffset(dto.Ticks - dto.Ticks % MicrosecondTicks, dto.Offset);
            case (SupportedDatabase.SingleStore or SupportedDatabase.Oracle, TimeSpan ts):
                return new TimeSpan(ts.Ticks - ts.Ticks % MicrosecondTicks);
            case (SupportedDatabase.SingleStore, TimeOnly to):
                return new TimeOnly(to.Ticks - to.Ticks % MicrosecondTicks);
            default:
                return value;
        }
    }

    // A parameter marker token: @name, :name (Oracle), $name (DuckDB), or a bare ? (positional). The lookbehind
    // keeps the second colon of a :: cast and the @ inside @name from starting a second token.
    private static readonly Regex MarkerToken = new(@"(?<![\w.:$@])(@\w+|:\w+|\$\w+|\?)", RegexOptions.Compiled);

    private static string StripLiterals(string expr) => Regex.Replace(expr, @"'[^']*'", "''");

    private static bool ContainsParameterMarker(string expr) => MarkerToken.IsMatch(StripLiterals(expr));

    // Rewrites INSERT INTO t (cols) SELECT list, with no FROM, as INSERT INTO t (cols) VALUES (list): the form
    // Snowflake uses. The list is then parsed like any VALUES tuple.
    private static string RewriteInsertSelectAsValues(string commandText)
    {
        var match = Regex.Match(commandText, @"^(\s*INSERT\s+INTO\s+[^(]+\([^)]+\)\s*)SELECT\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success || Regex.IsMatch(match.Groups[2].Value, @"\bFROM\b", RegexOptions.IgnoreCase))
        {
            return commandText;
        }

        return match.Groups[1].Value + "VALUES (" + match.Groups[2].Value.Trim() + ")";
    }

    // The top-level parenthesised groups in text, each without its outer parentheses. Quoted text and
    // nested parentheses are skipped, so CAST(@p AS varchar(10)) stays one group.
    private static List<string> ExtractTuples(string text)
    {
        var tuples = new List<string>();
        var depth = 0;
        var start = -1;
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != null)
            {
                if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
            }
            else if (c == '(')
            {
                if (depth == 0)
                {
                    start = i + 1;
                }

                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
                if (depth == 0)
                {
                    tuples.Add(text[start..i]);
                }
            }
        }

        return tuples;
    }

    // Splits on separator only at the top level: not inside parentheses or quotes.
    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var last = 0;
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != null)
            {
                if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == separator && depth == 0)
            {
                parts.Add(text[last..i]);
                last = i + 1;
            }
        }

        parts.Add(text[last..]);
        return parts;
    }

    // expr::VECTOR(INT, 0) and similar: a cast on the end of a value does not change what is stored.
    private static string StripTrailingCast(string expr)
    {
        var match = Regex.Match(expr, @"^(.+?)::\w+(?:\([^)]*\))?$", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : expr;
    }

    // expr.ST_AsEWKB() on a column reference reads the column: the conversion is the identity in fakeDb.
    private static string StripConversionCall(string expr)
    {
        var match = Regex.Match(expr, @"^(.+)\.\w+\(\)$", RegexOptions.Singleline);
        if (!match.Success)
        {
            return expr;
        }

        var segments = match.Groups[1].Value.Split('.');
        return segments[^1];
    }

    // FUNC(..., column, ...) reads the column: the conversion is the identity in fakeDb, so the column is the first
    // argument that is a column reference (CONVERT(VARCHAR(26), col, 140) and TO_VARCHAR(col, 'fmt') both qualify),
    // with any "AS type" suffix removed (CAST(col AS VARCHAR(10))).
    private static string ColumnOfExpression(string expr)
    {
        var match = Regex.Match(expr, @"^[A-Za-z_]\w*\((.*)\)$", RegexOptions.Singleline);
        if (!match.Success)
        {
            return expr;
        }

        foreach (var argument in SplitTopLevel(match.Groups[1].Value, ','))
        {
            var head = Regex.Replace(argument.Trim(), @"\s+AS\s+.*$", "", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(head, @"^(?:""?\w+""?\.)*""?\w+""?$"))
            {
                return head;
            }
        }

        return expr;
    }

    // The stored value of a server-side conversion over one bound value is that value. A conversion over two
    // different bound values has no single stored value, so it is refused rather than guessed.
    private object? EvaluateConversion(string expr, DbParameterCollection? parameters, ref int positional)
    {
        var tokens = MarkerToken.Matches(StripLiterals(expr)).Select(m => m.Value).ToList();
        var positionalCount = tokens.Count(t => t == "?");
        var named = tokens.Where(t => t != "?").Distinct().ToList();
        if ((positionalCount > 0 && named.Count > 0) || named.Count > 1 || positionalCount > 1)
        {
            throw new NotSupportedException(
                $"FakeDataStore cannot emulate a server-side expression over more than one bound value: '{expr}'.");
        }

        if (named.Count == 1)
        {
            return GetParameterValue(named[0], parameters);
        }

        object? value = null;
        if (parameters != null && positional < parameters.Count)
        {
            var raw = parameters[positional].Value;
            value = raw == DBNull.Value ? null : raw;
        }

        positional++;
        return value;
    }

    private static bool IsOracleSdoGeometry(string expression) =>
        expression.Contains("SDO_GEOMETRY(", StringComparison.OrdinalIgnoreCase) &&
        expression.Contains("TO_CLOB(", StringComparison.OrdinalIgnoreCase);

    private static bool IsSqlServerSpatialConstructor(string expression) =>
        expression.Contains("STGeomFromWKB(", StringComparison.OrdinalIgnoreCase);

    private static bool IsHanaArrayExpression(string expression) =>
        expression.StartsWith("ARRAY(SELECT", StringComparison.OrdinalIgnoreCase) &&
        expression.Contains("JSON_TABLE", StringComparison.OrdinalIgnoreCase);

    private List<object?> ParseValues(string valuesPart, DbParameterCollection? parameters, ref int positional)
    {
        _spatialUdtIndexes.Clear();
        var values = new List<object?>();
        var parts = SplitTopLevel(valuesPart, ',');

        foreach (var part in parts)
        {
            var trimmed = StripTrailingCast(part.Trim());

            // Handle parameters
            if (trimmed == "?")
            {
                // Positional marker (Access, Informix, HANA): the Nth marker is the Nth parameter.
                object? positionalValue = null;
                if (parameters != null && positional < parameters.Count)
                {
                    var raw = parameters[positional].Value;
                    positionalValue = raw == DBNull.Value ? null : raw;
                }

                positional++;
                values.Add(positionalValue);
            }
            else if (IsParameterMarker(trimmed))
            {
                var paramValue = GetParameterValue(trimmed, parameters);
                values.Add(paramValue);
            }
            // A server-side conversion over one bound value (CAST, PARSE_JSON, geometry::STGeomFromWKB,
            // ST_GeomFromEWKB, CASE WHEN @p IS NULL ... END): the stored value is the bound value.
            // SAP HANA's ARRAY(SELECT V FROM JSON_TABLE(?, ...)) over the bound JSON array: the stored value is the varbinary the
            // driver returns for an ARRAY column (HanaArrayWire), not the JSON text.
            // Oracle's SDO_GEOMETRY(wkt, srid) over the bound EWKT, in the scalar subquery the dialect writes: the stored value is the
            // EWKT, and SRID 0 (the unknown SRID) is NULL, as the dialect's NULLIF makes it.
            else if (IsOracleSdoGeometry(trimmed))
            {
                var ewkt = EvaluateConversion(trimmed, parameters, ref positional)?.ToString();
                values.Add(ewkt is null || Regex.IsMatch(ewkt, @"^SRID=0;", RegexOptions.IgnoreCase) ? null : ewkt);
            }
            // SQL Server's geometry::STGeomFromWKB(bytes, srid) (geography:: for a geography): the stored value is the native
            // encoding the spatial reader decodes (SqlServerSpatialWire), not the bound argument.
            else if (IsSqlServerSpatialConstructor(trimmed))
            {
                var geography = trimmed.Contains("geography::", StringComparison.OrdinalIgnoreCase);
                _spatialUdtIndexes[values.Count] = geography ? "geography" : "geometry";
                values.Add(SqlServerSpatialWire.Encode(EvaluateConversion(trimmed, parameters, ref positional), geography));
            }
            else if (IsHanaArrayExpression(trimmed))
            {
                var elementType = HanaArrayWire.ElementTypeOf(trimmed);
                values.Add(HanaArrayWire.Encode(EvaluateConversion(trimmed, parameters, ref positional), elementType));
            }
            else if (ContainsParameterMarker(trimmed))
            {
                values.Add(EvaluateConversion(trimmed, parameters, ref positional));
            }
            // Handle string literals
            else if (trimmed.StartsWith("'") && trimmed.EndsWith("'"))
            {
                values.Add(trimmed.Substring(1, trimmed.Length - 2));
            }
            // Handle numbers
            else if (int.TryParse(trimmed, out var intVal))
            {
                values.Add(intVal);
            }
            else if (double.TryParse(trimmed, out var doubleVal))
            {
                values.Add(doubleVal);
            }
            // Handle NULL
            else if (trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(null);
            }
            else
            {
                values.Add(trimmed);
            }
        }

        for (var i = 0; i < values.Count; i++)
        {
            values[i] = TruncateToStoredPrecision(values[i]);
        }

        return values;
    }

    private object? GetParameterValue(string paramName, DbParameterCollection? parameters)
    {
        if (parameters == null)
        {
            return null;
        }

        if (paramName == "?")
        {
            var index = _whereMarkerBase + _whereMarkerCursor++;
            return index < parameters.Count
                ? (parameters[index].Value == DBNull.Value ? null : parameters[index].Value)
                : null;
        }

        var bare = BareParameterName(paramName);
        foreach (DbParameter param in parameters)
        {
            if (param.ParameterName == paramName || BareParameterName(param.ParameterName) == bare)
            {
                return param.Value == DBNull.Value ? null : param.Value;
            }
        }

        return null;
    }

    private bool EvaluateWhereClause(Dictionary<string, object?> row, string whereClause,
        DbParameterCollection? parameters)
    {
        var trimmed = whereClause.Trim();

            // AND: split and evaluate each part
            var andParts = Regex.Split(trimmed, @"\s+AND\s+", RegexOptions.IgnoreCase);
            if (andParts.Length > 1)
            {
                return andParts.All(part => EvaluateWhereClause(row, part.Trim(), parameters));
            }

            // IS NOT NULL
            var isNotNullMatch = Regex.Match(trimmed,
                @"^([`\[\]""'\w.]+)\s+IS\s+NOT\s+NULL$", RegexOptions.IgnoreCase);
            if (isNotNullMatch.Success)
            {
                var col = ResolveRowKey(row, CleanIdentifier(isNotNullMatch.Groups[1].Value));
                return col != null && row[col] != null && !(row[col] is DBNull);
            }

            // IS NULL
            var isNullMatch = Regex.Match(trimmed,
                @"^([`\[\]""'\w.]+)\s+IS\s+NULL$", RegexOptions.IgnoreCase);
            if (isNullMatch.Success)
            {
                var col = ResolveRowKey(row, CleanIdentifier(isNullMatch.Groups[1].Value));
                return col == null || row[col] == null || row[col] is DBNull;
            }

            // LIKE
            var likeMatch = Regex.Match(trimmed,
                @"^([`\[\]""'\w.]+)\s+LIKE\s+(.+)$", RegexOptions.IgnoreCase);
            if (likeMatch.Success)
            {
                var col = ResolveRowKey(row, CleanIdentifier(likeMatch.Groups[1].Value));
                if (col == null)
                {
                    return false;
                }

                var pattern = GetCompareValue(likeMatch.Groups[2].Value.Trim(), parameters)?.ToString() ?? "";
                var rowVal = row[col]?.ToString() ?? "";
                return MatchesSqlLike(rowVal, pattern);
            }

            // IN: col IN (@p0, @p1, ...) or col IN ('a', 'b')
            var inMatch = Regex.Match(trimmed,
                @"^([`\[\]""'\w.]+)\s+IN\s*\(([^)]+)\)$", RegexOptions.IgnoreCase);
            if (inMatch.Success)
            {
                var col = ResolveRowKey(row, CleanIdentifier(inMatch.Groups[1].Value));
                if (col == null)
                {
                    return false;
                }

                var inValues = inMatch.Groups[2].Value.Split(',')
                    .Select(v => GetCompareValue(v.Trim(), parameters))
                    .ToList();
                return inValues.Any(v => Equals(row[col], v));
            }

            // Comparison operators: >=, <=, !=, <>, >, < (must check multi-char ops first)
            // Also handles simple equality (=) — matched last to avoid conflicting with >= etc.
            var cmpMatch = Regex.Match(trimmed,
                @"^([`\[\]""'\w.]+)\s*(>=|<=|!=|<>|>|<|=)\s*(.+)$", RegexOptions.IgnoreCase);
            if (cmpMatch.Success)
            {
                var col = ResolveRowKey(row, CleanIdentifier(cmpMatch.Groups[1].Value));
                if (col == null)
                {
                    return false;
                }

                var op = cmpMatch.Groups[2].Value;
                var compareValue = GetCompareValue(cmpMatch.Groups[3].Value.Trim(), parameters);
                return EvaluateComparison(row[col], op, compareValue);
            }

        // Unrecognized predicate — throw so tests don't silently pass with no filtering
        throw new NotSupportedException(
            $"FakeDataStore: WHERE predicate not supported: '{trimmed}'. " +
            "Extend EvaluateWhereClause to handle this pattern.");
    }

    private static bool EvaluateComparison(object? left, string op, object? right)
    {
        if (left == null || right == null)
        {
            // NULL comparisons: only = and <>/!= are defined
            return op is "=" ? left == right : (op is "!=" or "<>") && left != right;
        }

        // Attempt numeric comparison first; fall back to string comparison
        if (TryToDouble(left, out var ld) && TryToDouble(right, out var rd))
        {
            return op switch
            {
                "="  => ld == rd,
                "!=" or "<>" => ld != rd,
                ">"  => ld > rd,
                ">=" => ld >= rd,
                "<"  => ld < rd,
                "<=" => ld <= rd,
                _    => false
            };
        }

        var ls = left.ToString() ?? "";
        var rs = right.ToString() ?? "";
        var cmp = string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "="  => cmp == 0,
            "!=" or "<>" => cmp != 0,
            ">"  => cmp > 0,
            ">=" => cmp >= 0,
            "<"  => cmp < 0,
            "<=" => cmp <= 0,
            _    => false
        };
    }

    private static bool TryToDouble(object value, out double result)
    {
        try
        {
            result = Convert.ToDouble(value);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private string? ResolveRowKey(Dictionary<string, object?> row, string column)
    {
        if (row.ContainsKey(column))
        {
            return column;
        }

        return row.Keys.FirstOrDefault(k => k.Equals(column, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesSqlLike(string value, string pattern)
    {
        // Convert SQL LIKE wildcards to regex char-by-char so % → .* and _ → .
        // without relying on Regex.Escape to escape % (it doesn't).
        var sb = new System.Text.StringBuilder("^");
        foreach (var c in pattern)
        {
            switch (c)
            {
                case '%': sb.Append(".*"); break;
                case '_': sb.Append('.'); break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        sb.Append('$');
        return Regex.IsMatch(value, sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private object? GetCompareValue(string valueExpression, DbParameterCollection? parameters)
    {
        var trimmed = valueExpression.Trim();

        // Handle parameters
        if (IsParameterMarker(trimmed))
        {
            return GetParameterValue(trimmed, parameters);
        }
        // Handle string literals
        else if (trimmed.StartsWith("'") && trimmed.EndsWith("'"))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }
        // Handle numbers
        else if (int.TryParse(trimmed, out var intVal))
        {
            return intVal;
        }
        else if (double.TryParse(trimmed, out var doubleVal))
        {
            return doubleVal;
        }
        // Handle NULL
        else if (trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private void ApplySetClause(Dictionary<string, object?> row, string setClause, DbParameterCollection? parameters)
    {
        // Simple SET clause parsing - handles "column = value, column2 = value2". The column-name
        // character class matches EvaluateWhereClause's (backtick/bracket/double-quote/single-quote
        // plus word chars) so a real dialect-generated UPDATE — every dialect quotes identifiers by
        // default, e.g. "column" = @p0 — is recognized here too, not just a bare, unquoted name.
        var assignments = setClause.Split(',');
        foreach (var assignment in assignments)
        {
            var match = Regex.Match(assignment, @"([`\[\]""'\w.]+)\s*=\s*(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var column = ResolveRowKey(row, CleanIdentifier(match.Groups[1].Value.Trim())) ??
                             CleanIdentifier(match.Groups[1].Value.Trim());
                var valueExpression = match.Groups[2].Value.Trim();
                var value = TryEvaluateColumnArithmetic(row, valueExpression, parameters, out var computed)
                    ? computed
                    : GetCompareValue(valueExpression, parameters);
                row[column] = value;
            }
        }
    }

    // Evaluates "column + operand" / "column - operand" against the row's current value, e.g. the
    // "version" = "version" + 1 increment every versioned UPDATE emits. Returns false (so the caller
    // falls back to plain value parsing) when the left side isn't a column of this row or either
    // side isn't numeric.
    private bool TryEvaluateColumnArithmetic(Dictionary<string, object?> row, string valueExpression,
        DbParameterCollection? parameters, out object? result)
    {
        result = null;
        var match = Regex.Match(valueExpression, @"^([`\[\]""\w.]+)\s*([+-])\s*(.+)$");
        if (!match.Success)
        {
            return false;
        }

        var sourceKey = ResolveRowKey(row, CleanIdentifier(match.Groups[1].Value.Trim()));
        if (sourceKey == null)
        {
            return false;
        }

        var left = row[sourceKey];
        var right = GetCompareValue(match.Groups[3].Value, parameters);
        if (left == null || right == null || right is string)
        {
            return false;
        }

        var subtract = match.Groups[2].Value == "-";
        if (IsIntegral(left) && IsIntegral(right))
        {
            var l = Convert.ToInt64(left, CultureInfo.InvariantCulture);
            var r = Convert.ToInt64(right, CultureInfo.InvariantCulture);
            result = subtract ? l - r : l + r;
            return true;
        }

        if (!TryToDouble(left, out var ld) || !TryToDouble(right, out var rd))
        {
            return false;
        }

        result = subtract ? ld - rd : ld + rd;
        return true;
    }

    private static bool IsIntegral(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong;

    private IEnumerable<Dictionary<string, object?>> HandleLiteralSelect(string selectPart,
        DbParameterCollection? parameters)
    {
        // Parse "1 as id, 'test' as name, 42 as value" into columns and values
        var columnValuePairs = selectPart.Split(',');
        var row = new Dictionary<string, object?>();

        foreach (var pair in columnValuePairs)
        {
            var trimmed = pair.Trim();

            // Handle "value AS alias" or "value alias" patterns
            var match = Regex.Match(trimmed, @"^(.+?)\s+(?:AS\s+)?([a-zA-Z_][a-zA-Z0-9_]*)$", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var valueExpression = match.Groups[1].Value.Trim();
                var columnName = match.Groups[2].Value.Trim();
                var value = GetCompareValue(valueExpression, parameters);
                row[columnName] = value;
            }
            else
            {
                // Handle simple values without aliases
                var value = GetCompareValue(trimmed, parameters);
                row[$"Column{row.Count + 1}"] = value;
            }
        }

        return new[] { row };
    }
}
