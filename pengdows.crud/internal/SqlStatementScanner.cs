// =============================================================================
// FILE: SqlStatementScanner.cs
// PURPOSE: Tells whether SQL text holds more than one statement.
//
// AI SUMMARY:
// - HasMultipleStatements(): true when a ';' outside quotes and comments is followed by more SQL.
//   A trailing ';' (with only whitespace, comments or more ';' after it) is still one statement.
// - Skips '...' strings ('' escapes), "..." identifiers, PostgreSQL $tag$...$tag$ bodies, -- line
//   comments and /* */ block comments (nested, as PostgreSQL nests them).
// - Used to skip Prepare() where the dialect can't prepare a multi-statement command
//   (SqlDialect.PreparesMultiStatementCommands; CockroachDB).
// =============================================================================

namespace pengdows.crud.@internal;

/// <summary>
/// Finds statement boundaries in SQL text without parsing it.
/// </summary>
internal static class SqlStatementScanner
{
    public static bool HasMultipleStatements(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var separatorSeen = false;
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '-' && Next(sql, i) == '-')
            {
                i = SkipLineComment(sql, i);
                continue;
            }

            if (c == '/' && Next(sql, i) == '*')
            {
                i = SkipBlockComment(sql, i);
                continue;
            }

            if (c == ';')
            {
                separatorSeen = true;
                i++;
                continue;
            }

            // Anything else is SQL: after a separator, it starts a second statement.
            if (separatorSeen)
            {
                return true;
            }

            i = c switch
            {
                '\'' => SkipQuoted(sql, i, '\''),
                '"' => SkipQuoted(sql, i, '"'),
                '$' => SkipDollarQuoted(sql, i),
                _ => i + 1
            };
        }

        return false;
    }

    private static char Next(string sql, int i) => i + 1 < sql.Length ? sql[i + 1] : '\0';

    private static int SkipLineComment(string sql, int i)
    {
        var end = sql.IndexOf('\n', i);
        return end < 0 ? sql.Length : end + 1;
    }

    private static int SkipBlockComment(string sql, int i)
    {
        var depth = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '/' && Next(sql, i) == '*')
            {
                depth++;
                i += 2;
            }
            else if (sql[i] == '*' && Next(sql, i) == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        return sql.Length;
    }

    // A doubled quote inside is an escaped quote ('it''s', "a""b").
    private static int SkipQuoted(string sql, int i, char quote)
    {
        i++;
        while (i < sql.Length)
        {
            if (sql[i] == quote)
            {
                if (Next(sql, i) == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return sql.Length;
    }

    // $$...$$ or $tag$...$tag$; a $ that doesn't open a tag ($1, a positional parameter) is one char.
    private static int SkipDollarQuoted(string sql, int i)
    {
        var tagEnd = i + 1;
        while (tagEnd < sql.Length && (char.IsLetterOrDigit(sql[tagEnd]) || sql[tagEnd] == '_'))
        {
            tagEnd++;
        }

        if (tagEnd >= sql.Length || sql[tagEnd] != '$' || (tagEnd > i + 1 && char.IsDigit(sql[i + 1])))
        {
            return i + 1;
        }

        var tag = sql.Substring(i, tagEnd - i + 1);
        var close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
        return close < 0 ? sql.Length : close + tag.Length;
    }
}
