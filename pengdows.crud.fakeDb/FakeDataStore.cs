#region

using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// In-memory data store for FakeDb that provides automatic data persistence
/// Simulates database behavior by storing INSERT data and returning it on SELECT
/// </summary>
public class FakeDataStore
{
    // Table_name -> List of rows (each row is a Dictionary of column->value)
    private readonly ConcurrentDictionary<string, List<Dictionary<string, object?>>> _tables = new();
    private readonly object _lockObject = new();
    private int _nextId = 1;
    private int _lastInsertId = 0;

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
        return 1;
    }

    public object? ExecuteScalar(string commandText, DbParameterCollection? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
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
                return firstRow.Values.FirstOrDefault();
            }
        }

        return null;
    }

    public int LastInsertId => _lastInsertId;

    public IEnumerable<Dictionary<string, object?>> ExecuteReader(string commandText,
        DbParameterCollection? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return Enumerable.Empty<Dictionary<string, object?>>();
        }

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
                lock (_lockObject) { _tables[tableName].Add(row); }
                return 1;
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
        if (trailingClause.Success)
        {
            afterValues = afterValues.Substring(0, trailingClause.Index);
        }

        var valueRowMatches = Regex.Matches(afterValues, @"\(([^)]+)\)");

        var rowsInserted = 0;
        foreach (Match rowMatch in valueRowMatches)
        {
            var values = ParseValues(rowMatch.Groups[1].Value, parameters);
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
                    try { _lastInsertId = Convert.ToInt32(values[i]); } catch { /* non-numeric id */ }
                }
            }

            lock (_lockObject) { _tables[table].Add(rowData); }
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

        // Handle SELECT * FROM table [alias] [WHERE ...] [ORDER BY ...]
        var selectMatch = Regex.Match(commandText,
            @"SELECT\s+([\s\S]+?)\s+FROM\s+([`\[\]""'.\w]+)(?:\s+(?:AS\s+)?[`\[\]""'\w]+)?(?:\s+WHERE\s+([\s\S]+?))?(?:\s+ORDER\s+BY\s+.+)?$",
            RegexOptions.IgnoreCase);

        if (!selectMatch.Success)
        {
            return Enumerable.Empty<Dictionary<string, object?>>();
        }

        var selectPart = selectMatch.Groups[1].Value.Trim();
        var tableName = CleanIdentifier(selectMatch.Groups[2].Value);
        var wherePart = selectMatch.Groups.Count > 3 ? selectMatch.Groups[3].Value : null;

        EnsureTable(tableName);

        lock (_lockObject)
        {
            var rows = _tables[tableName];
            var filteredRows = rows.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(wherePart))
            {
                filteredRows = filteredRows.Where(row => EvaluateWhereClause(row, wherePart, parameters));
            }

            // Handle column selection
            if (selectPart == "*")
            {
                return filteredRows.ToList();
            }

            // Handle specific columns
            var columns = selectPart.Split(',').Select(c => CleanIdentifier(c.Trim())).ToList();
            return filteredRows.Select(row =>
            {
                var result = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    if (row.ContainsKey(col))
                    {
                        result[col] = row[col];
                    }
                }

                return result;
            }).ToList();
        }
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

    private List<object?> ParseValues(string valuesPart, DbParameterCollection? parameters)
    {
        var values = new List<object?>();
        var parts = valuesPart.Split(',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            // Handle parameters
            if (trimmed.StartsWith("@") || trimmed.StartsWith("?"))
            {
                var paramValue = GetParameterValue(trimmed, parameters);
                values.Add(paramValue);
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

        return values;
    }

    private object? GetParameterValue(string paramName, DbParameterCollection? parameters)
    {
        if (parameters == null)
        {
            return null;
        }

        foreach (DbParameter param in parameters)
        {
            if (param.ParameterName == paramName || param.ParameterName == paramName.TrimStart('@', '?'))
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
        if (trimmed.StartsWith("@") || trimmed.StartsWith("?"))
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
        // Simple SET clause parsing - handles "column = value, column2 = value2"
        var assignments = setClause.Split(',');
        foreach (var assignment in assignments)
        {
            var match = Regex.Match(assignment, @"(\w+)\s*=\s*(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var column = match.Groups[1].Value.Trim();
                var valueExpression = match.Groups[2].Value.Trim();
                var value = GetCompareValue(valueExpression, parameters);
                row[column] = value;
            }
        }
    }

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