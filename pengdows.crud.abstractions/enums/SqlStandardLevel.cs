namespace pengdows.crud.enums;

/// <summary>
/// SQL feature capability tier indicating roughly which SQL features are available.
/// <para><strong>IMPORTANT:</strong> This is NOT a measure of ISO SQL standard conformance.</para>
/// <para>
/// Values indicate the approximate SQL language era and feature set supported by the database,
/// such as CTEs, window functions, JSON support, etc. Database vendors implement features
/// from various standard versions inconsistently, so this is a heuristic used by pengdows.crud
/// to estimate feature availability, not a claim of standards compliance.
/// </para>
/// </summary>
/// <remarks>
/// For example, MySQL 8 maps to Sql2008 because it supports CTEs and window functions that
/// became common in that era, but MySQL diverges significantly from ISO SQL:2008 in areas like
/// type system, constraint semantics, transactional DDL, stored procedures, and information schema.
/// </remarks>
public enum SqlStandardLevel
{
    Sql86 = 1986,
    Sql89 = 1989,
    Sql92 = 1992,
    Sql99 = 1999,
    Sql2003 = 2003,
    Sql2006 = 2006,
    Sql2008 = 2008,
    Sql2011 = 2011,
    Sql2016 = 2016,
    Sql2019 = 2019,
    Sql2023 = 2023
}
