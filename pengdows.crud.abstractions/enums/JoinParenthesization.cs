namespace pengdows.crud.enums;

/// <summary>
/// Defines whether a dialect requires, allows, or forbids parenthesizing a nested join
/// expression in a FROM clause (e.g. <c>A JOIN (B JOIN C ON ...) ON ...</c>).
/// </summary>
public enum JoinParenthesization
{
    /// <summary>
    /// Parenthesizing a nested join is syntactically valid but not required.
    /// The behavior of most ANSI SQL databases.
    /// </summary>
    Optional = 0,

    /// <summary>
    /// Parenthesizing a nested join is mandatory once more than two tables are joined.
    /// Example: MS Access/Jet's query engine rejects an unparenthesized three-table
    /// join with a syntax error.
    /// </summary>
    Required = 1,

    /// <summary>
    /// The dialect's parser rejects a parenthesized nested join outright; joins must
    /// be written as a flat left-to-right chain.
    /// </summary>
    Forbidden = 2
}
