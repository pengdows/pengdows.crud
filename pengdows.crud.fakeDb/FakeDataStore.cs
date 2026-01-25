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

    public IEnumerable<Dictionary<string, object?>> ExecuteReader(string commandText,
        DbParameterCollection? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return Enumerable.Empty<Dictionary<string, object?>>();
        }

        var sql = commandText.Trim().ToUpperInvariant();

        // Handle SELECT queries
        if (sql.StartsWith("SELECT"))
        {
            return HandleSelect(commandText, parameters);
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
        try
        {
            // Simple INSERT parsing - matches "INSERT INTO table_name (col1, col2) VALUES (val1, val2)"
            var insertMatch = Regex.Match(commandText,
                @"INSERT\s+INTO\s+([`\[\]""'\w]+)\s*\(([^)]+)\)\s*VALUES\s*\(([^)]+)\)",
                RegexOptions.IgnoreCase);

            if (!insertMatch.Success)
            {
                // Try simple INSERT INTO table VALUES format
                var simpleMatch = Regex.Match(commandText,
                    @"INSERT\s+INTO\s+([`\[\]""'\w]+)\s+VALUES\s*\(([^)]+)\)",
                    RegexOptions.IgnoreCase);
                if (simpleMatch.Success)
                {
                    var tableName = CleanIdentifier(simpleMatch.Groups[1].Value);
                    EnsureTable(tableName);

                    // For simple format, create a generic row with auto-assigned ID
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

                return 1; // Return 1 for unrecognized INSERT formats
            }

            var table = CleanIdentifier(insertMatch.Groups[1].Value);
            var columnsPart = insertMatch.Groups[2].Value;
            var valuesPart = insertMatch.Groups[3].Value;

            EnsureTable(table);

            // Parse columns
            var columns = columnsPart.Split(',')
                .Select(c => CleanIdentifier(c.Trim()))
                .ToList();

            // Parse values (handle parameters and literals)
            var values = ParseValues(valuesPart, parameters);

            // Create row data
            var rowData = new Dictionary<string, object?>();

            // Auto-assign ID if not provided
            if (!columns.Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase)))
            {
                var nextId = _nextId++;
                _lastInsertId = nextId;
                rowData["Id"] = nextId;
            }

            for (var i = 0; i < Math.Min(columns.Count, values.Count); i++)
            {
                rowData[columns[i]] = values[i];

                // Track the last insert ID if an ID column was explicitly provided
                if (columns[i].Equals("Id", StringComparison.OrdinalIgnoreCase) && values[i] is int explicitId)
                {
                    _lastInsertId = explicitId;
                }
            }

            lock (_lockObject)
            {
                _tables[table].Add(rowData);
            }

            return 1;
        }
        catch
        {
            return 1; // Return 1 even if parsing fails
        }
    }

    private int HandleUpdate(string commandText, DbParameterCollection? parameters)
    {
        try
        {
            // Simple UPDATE parsing
            var updateMatch = Regex.Match(commandText,
                @"UPDATE\s+([`\[\]""'\w]+)\s+SET\s+(.+?)(?:\s+WHERE\s+(.+))?$",
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
        catch
        {
            return 0;
        }
    }

    private int HandleDelete(string commandText, DbParameterCollection? parameters)
    {
        try
        {
            // Simple DELETE parsing
            var deleteMatch = Regex.Match(commandText,
                @"DELETE\s+FROM\s+([`\[\]""'\w]+)(?:\s+WHERE\s+(.+))?$",
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
        catch
        {
            return 0;
        }
    }

    private IEnumerable<Dictionary<string, object?>> HandleSelect(string commandText, DbParameterCollection? parameters)
    {
        try
        {
            // Handle literal SELECT without FROM (e.g., SELECT 1 as id, 'test' as name)
            var literalMatch = Regex.Match(commandText,
                @"^SELECT\s+(.+?)(?:\s+FROM\s|$)", RegexOptions.IgnoreCase);

            if (literalMatch.Success && !commandText.Contains("FROM", StringComparison.OrdinalIgnoreCase))
            {
                return HandleLiteralSelect(literalMatch.Groups[1].Value.Trim(), parameters);
            }

            // Handle SELECT * FROM table
            var selectMatch = Regex.Match(commandText,
                @"SELECT\s+([\s\S]+?)\s+FROM\s+([`\[\]""'\w]+)(?:\s+WHERE\s+([\s\S]+?))?(?:\s+ORDER\s+BY\s+.+)?$",
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
                else
                {
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
        }
        catch
        {
            return Enumerable.Empty<Dictionary<string, object?>>();
        }
    }

    private object? HandleCountQuery(string commandText, DbParameterCollection? parameters)
    {
        try
        {
            // Extract table name from COUNT query
            var countMatch = Regex.Match(commandText,
                @"SELECT\s+COUNT\([^)]*\)\s+FROM\s+([`\[\]""'\w]+)(?:\s+WHERE\s+(.+))?$",
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
                else
                {
                    return (long)rows.Count(row => EvaluateWhereClause(row, wherePart, parameters));
                }
            }
        }
        catch
        {
            return 0;
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
        // Simple WHERE clause evaluation - handles basic equality checks
        try
        {
            // Handle "column = value" or "column = @param"
            var match = Regex.Match(whereClause, @"(\w+)\s*=\s*(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var column = match.Groups[1].Value.Trim();
                var valueExpression = match.Groups[2].Value.Trim();

                if (!row.ContainsKey(column))
                {
                    return false;
                }

                var rowValue = row[column];
                var compareValue = GetCompareValue(valueExpression, parameters);

                return Equals(rowValue, compareValue);
            }

            // Default to true for complex WHERE clauses we can't parse
            return true;
        }
        catch
        {
            return true;
        }
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