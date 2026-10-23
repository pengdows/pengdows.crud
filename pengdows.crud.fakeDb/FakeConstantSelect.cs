using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace pengdows.crud.fakeDb;

/// <summary>
/// Evaluates a batch of SELECTs that read no table — literals, NULL and bound parameters, optionally joined by UNION and
/// ordered by column number — the way a database answers them: one result set per statement, comments ignored. It
/// also names a command's first keyword, so the data store can tell text that is not SQL at all from SQL it does not
/// model.
/// </summary>
internal static class FakeConstantSelect
{
    private static readonly HashSet<string> StatementKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALTER", "ANALYZE", "BEGIN", "CALL", "COMMIT", "CREATE", "DECLARE", "DELETE", "DROP", "EXEC", "EXECUTE",
        "EXPLAIN", "GRANT", "INSERT", "LOCK", "MERGE", "PRAGMA", "RELEASE", "REPLACE", "REVOKE", "ROLLBACK",
        "SAVEPOINT", "SELECT", "SET", "SHOW", "TRUNCATE", "UPDATE", "UPSERT", "USE", "VACUUM", "VALUES", "WITH"
    };

    private static readonly Regex OrderByColumn =
        new(@"\s+ORDER\s+BY\s+(\d+)(?:\s+(ASC|DESC))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Union =
        new(@"\s+UNION(\s+ALL)?\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SelectList =
        new(@"^SELECT\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex From = new(@"\bFROM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Alias =
        new(@"^(.+?)\s+(?:AS\s+)?([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline |
                                                               RegexOptions.CultureInvariant);

    /// <summary>The command's first keyword after comments, or null for a command with no statement in it.</summary>
    internal static string? FirstKeyword(string commandText)
    {
        var start = SkipLeadingTrivia(commandText);
        if (start == commandText.Length)
        {
            return null;
        }

        // An ODBC call escape ({call proc(?)}) is a procedure call.
        if (commandText[start] == '{')
        {
            return "CALL";
        }

        var end = start;
        while (end < commandText.Length && !char.IsWhiteSpace(commandText[end]) && commandText[end] is not (';' or '('))
        {
            end++;
        }

        return commandText.Substring(start, Math.Max(end - start, 1));
    }

    /// <summary>
    /// True when the command's first keyword is <paramref name="keyword"/>. Reads the text in place: commands can be
    /// large, and the library's allocation tests run through fakeDb.
    /// </summary>
    internal static bool StartsWithKeyword(string commandText, string keyword)
    {
        var start = SkipLeadingTrivia(commandText);
        var text = commandText.AsSpan(start);
        return text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) &&
               (text.Length == keyword.Length || !(char.IsLetterOrDigit(text[keyword.Length]) || text[keyword.Length] == '_'));
    }

    // The position of the first character after leading whitespace, opening parentheses and comments.
    private static int SkipLeadingTrivia(string sql)
    {
        var i = 0;
        while (i < sql.Length)
        {
            if (char.IsWhiteSpace(sql[i]) || sql[i] == '(')
            {
                i++;
            }
            else if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var newline = sql.IndexOf('\n', i);
                i = newline < 0 ? sql.Length : newline + 1;
            }
            else if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? sql.Length : close + 2;
            }
            else
            {
                break;
            }
        }

        return i;
    }

    /// <summary>True when the command starts with something no database accepts as a statement.</summary>
    internal static bool IsNotSql(string commandText)
    {
        var keyword = FirstKeyword(commandText);
        return keyword != null && !StatementKeywords.Contains(keyword);
    }

    /// <summary>
    /// The command's result sets when every statement in it is a constant SELECT; false for anything else (a table read,
    /// a function call, a parameter the command does not have), which the data store answers as before.
    /// </summary>
    internal static bool TryEvaluate(string commandText, DbParameterCollection? parameters,
        out List<List<Dictionary<string, object?>>> resultSets)
    {
        resultSets = new List<List<Dictionary<string, object?>>>();
        if (!StartsWithKeyword(commandText, "SELECT"))
        {
            return false;
        }

        var statements = SplitStatements(StripComments(commandText));
        if (statements.Count == 0)
        {
            return false;
        }

        var positional = 0;
        foreach (var statement in statements)
        {
            var rows = TryEvaluateStatement(statement, parameters, ref positional);
            if (rows == null)
            {
                resultSets.Clear();
                return false;
            }

            resultSets.Add(rows);
        }

        return true;
    }

    private static List<Dictionary<string, object?>>? TryEvaluateStatement(string statement,
        DbParameterCollection? parameters, ref int positional)
    {
        var masked = MaskLiterals(statement);
        if (From.IsMatch(masked))
        {
            return null;
        }

        int? orderColumn = null;
        var descending = false;
        var order = OrderByColumn.Match(masked);
        if (order.Success)
        {
            orderColumn = int.Parse(order.Groups[1].Value, CultureInfo.InvariantCulture);
            descending = string.Equals(order.Groups[2].Value, "DESC", StringComparison.OrdinalIgnoreCase);
            statement = statement[..order.Index];
            masked = masked[..order.Index];
        }

        var rows = new List<Dictionary<string, object?>>();
        var distinct = false;
        var start = 0;
        foreach (Match union in Union.Matches(masked))
        {
            if (!TryAddRow(statement[start..union.Index], masked[start..union.Index], parameters, ref positional, rows))
            {
                return null;
            }

            distinct |= !union.Groups[1].Success;
            start = union.Index + union.Length;
        }

        if (!TryAddRow(statement[start..], masked[start..], parameters, ref positional, rows))
        {
            return null;
        }

        if (distinct)
        {
            rows = rows.DistinctBy(row => string.Join("\u0001", row.Values.Select(Key))).ToList();
        }

        if (orderColumn is { } column)
        {
            if (column < 1 || rows.Count == 0 || column > rows[0].Count)
            {
                return null;
            }

            var ordered = rows.OrderBy(row => row.Values.ElementAt(column - 1), ValueComparer.Instance);
            rows = (descending ? ordered.Reverse() : ordered).ToList();
        }

        return rows;
    }

    private static bool TryAddRow(string select, string masked, DbParameterCollection? parameters, ref int positional,
        List<Dictionary<string, object?>> rows)
    {
        var match = SelectList.Match(masked.Trim());
        if (!match.Success)
        {
            return false;
        }

        var offset = masked.Length - masked.TrimStart().Length + match.Groups[1].Index;
        var list = select[offset..].Trim();
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in SplitTopLevel(list, MaskLiterals(list), ','))
        {
            var expression = item.Trim();
            string? alias = null;
            if (!TryEvaluateExpression(expression, parameters, ref positional, out var value))
            {
                var aliased = Alias.Match(MaskLiterals(expression));
                if (!aliased.Success ||
                    !TryEvaluateExpression(expression[..aliased.Groups[1].Length], parameters, ref positional, out value))
                {
                    return false;
                }

                alias = aliased.Groups[2].Value;
                expression = expression[..aliased.Groups[1].Length];
            }

            var name = alias ?? expression;
            var unique = name;
            for (var n = 1; row.ContainsKey(unique); n++)
            {
                unique = $"{name}:{n}";
            }

            row[unique] = value;
        }

        // Every row of a UNION takes its column names from the first SELECT.
        if (rows.Count > 0)
        {
            var names = rows[0].Keys.ToList();
            if (names.Count != row.Count)
            {
                return false;
            }

            row = names.Zip(row.Values).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.OrdinalIgnoreCase);
        }

        rows.Add(row);
        return true;
    }

    private static bool TryEvaluateExpression(string expression, DbParameterCollection? parameters, ref int positional,
        out object? value)
    {
        value = null;
        var text = expression.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (text.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            value = DBNull.Value;
            return true;
        }

        if (Regex.IsMatch(text, @"^'(?:[^']|'')*'$", RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            value = text[1..^1].Replace("''", "'");
            return true;
        }

        if (Regex.IsMatch(text, @"^[+-]?\d+$", RegexOptions.CultureInvariant))
        {
            value = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? (object)i
                : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? l
                    : decimal.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return true;
        }

        if (Regex.IsMatch(text, @"^[+-]?(\d+\.\d*|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant))
        {
            value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }

        return TryBindParameter(text, parameters, ref positional, out value);
    }

    private static bool TryBindParameter(string text, DbParameterCollection? parameters, ref int positional,
        out object? value)
    {
        value = null;
        if (parameters == null)
        {
            return false;
        }

        DbParameter? bound = null;
        if (text == "?")
        {
            if (positional >= parameters.Count)
            {
                return false;
            }

            bound = parameters[positional++];
        }
        else if (Regex.IsMatch(text, @"^[@:$?][A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
        {
            var name = text[1..];
            foreach (DbParameter parameter in parameters)
            {
                // A driver may leave a new parameter's name null (fakeDbDriverQuirks.ParameterNameDefaultsToNull).
                var parameterName = parameter.ParameterName ?? string.Empty;
                if (parameterName == text || parameterName.TrimStart('@', ':', '$', '?') == name)
                {
                    bound = parameter;
                    break;
                }
            }
        }

        if (bound == null)
        {
            return false;
        }

        value = bound.Value ?? DBNull.Value;
        if (!HasDatabaseType(value))
        {
            // A real provider cannot send a value it has no wire type for (SqlClient: "No mapping exists from object
            // type X to a known managed provider native type.").
            throw new UnmappableValueException(value);
        }

        return true;
    }

    private static bool HasDatabaseType(object value)
    {
        return value is DBNull or string or char or bool or byte or sbyte or short or ushort or int or uint or long
                   or ulong or float or double or decimal or DateTime or DateTimeOffset or TimeSpan or DateOnly
                   or TimeOnly or Guid or byte[] or Enum or Array or Stream or fakeDbInterval or fakeDbProviderDecimal;
    }

    private static string Key(object? value)
    {
        return value switch
        {
            null or DBNull => "\u0000",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>Removes -- and /* */ comments outside string literals and quoted identifiers.</summary>
    internal static string StripComments(string sql)
    {
        var result = new StringBuilder(sql.Length);
        char? quote = null;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (quote != null)
            {
                result.Append(c);
                if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                result.Append(c);
            }
            else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                result.Append('\n');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 1;
                result.Append(' ');
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static List<string> SplitStatements(string sql)
    {
        return SplitTopLevel(sql, MaskLiterals(sql), ';')
            .Select(statement => statement.Trim())
            .Where(statement => statement.Length > 0)
            .ToList();
    }

    // Splits on a separator outside literals and parentheses; masked is text with the same length whose literals are
    // blanked, so the positions found there are positions in text.
    private static IEnumerable<string> SplitTopLevel(string text, string masked, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < masked.Length; i++)
        {
            switch (masked[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                default:
                    if (masked[i] == separator && depth == 0)
                    {
                        yield return text[start..i];
                        start = i + 1;
                    }

                    break;
            }
        }

        yield return text[start..];
    }

    // The text with every character inside a '...' or "..." literal replaced, so keyword and separator searches cannot
    // match inside one. Same length as the input.
    private static string MaskLiterals(string sql)
    {
        var chars = sql.ToCharArray();
        char? quote = null;
        for (var i = 0; i < chars.Length; i++)
        {
            if (quote != null)
            {
                if (chars[i] == quote)
                {
                    quote = null;
                }
                else
                {
                    chars[i] = '_';
                }
            }
            else if (chars[i] is '\'' or '"')
            {
                quote = chars[i];
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// The contract's <see cref="NotSupportedException"/> for a value no database type can hold; carries the value so a
    /// command can raise its driver's own error instead (<see cref="fakeDbDriverQuirks.UnmappableParameterValueException"/>).
    /// </summary>
    internal sealed class UnmappableValueException(object value) : NotSupportedException(
        $"No mapping exists from object type {value.GetType().FullName} to a known managed provider native type.")
    {
        internal object Value { get; } = value;
    }

    private sealed class ValueComparer : IComparer<object?>
    {
        internal static readonly ValueComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            var xNull = x is null or DBNull;
            var yNull = y is null or DBNull;
            if (xNull || yNull)
            {
                return xNull.CompareTo(yNull) * -1;
            }

            if (x is IConvertible && y is IConvertible && x is not string && y is not string)
            {
                return Convert.ToDecimal(x, CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToDecimal(y, CultureInfo.InvariantCulture));
            }

            return Comparer.Default.Compare(x, y);
        }
    }
}
