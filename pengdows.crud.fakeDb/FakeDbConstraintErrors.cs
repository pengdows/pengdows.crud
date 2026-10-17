using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.fakeDb;

/// <summary>
/// The unique-key violation each emulated product's provider raises: the SQLSTATE, vendor error number and message the
/// library's dialects classify on. Provider-shaped, so internal.
/// </summary>
internal static class FakeDbConstraintErrors
{
    internal static DbException UniqueViolation(SupportedDatabase product, string table, string constraint)
    {
        var (message, number, sqlState) = product switch
        {
            SupportedDatabase.SqlServer => ($"Violation of PRIMARY KEY constraint '{constraint}'. Cannot insert duplicate key in object '{table}'.", 2627, null),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb or SupportedDatabase.AuroraMySql
                or SupportedDatabase.SingleStore => ($"Duplicate entry for key '{constraint}'", 1062, "23000"),
            SupportedDatabase.Oracle => ($"ORA-00001: unique constraint ({constraint}) violated", 1, null),
            SupportedDatabase.Sqlite => ($"UNIQUE constraint failed: {table}", 2067, null),
            SupportedDatabase.DuckDB => ($"Constraint Error: Duplicate key violates primary key constraint '{constraint}'", 0, null),
            SupportedDatabase.Firebird => ($"violation of PRIMARY or UNIQUE KEY constraint \"{constraint}\" on table \"{table}\"", 335544665, null),
            SupportedDatabase.InterBase => ($"violation of PRIMARY or UNIQUE KEY constraint \"{constraint}\" on table \"{table}\"", 335544665, null),
            SupportedDatabase.Db2 => ($"SQL0803N One or more values in the INSERT statement violate a unique constraint: {constraint}", -803, "23505"),
            SupportedDatabase.Informix => ("Unique constraint violated.", -268, "23000"),
            SupportedDatabase.SapHana => ($"unique constraint violated: {constraint}", 301, null),
            SupportedDatabase.SybaseASE => ($"Attempt to insert duplicate key row in object '{table}' with unique index '{constraint}'", 2601, null),
            SupportedDatabase.Snowflake => ($"Duplicate key value violates unique constraint \"{constraint}\"", 0, null),
            SupportedDatabase.Access => ("The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship.", 0, null),
            _ => ($"duplicate key value violates unique constraint \"{constraint}\"", 0, "23505")
        };

        return new ProviderShapedException(message, number, sqlState, product == SupportedDatabase.Sqlite ? 2067 : 0);
    }

    private sealed class ProviderShapedException : DbException
    {
        private readonly string? _sqlState;

        internal ProviderShapedException(string message, int number, string? sqlState, int errorCode)
            : base(message, errorCode)
        {
            Number = number;
            _sqlState = sqlState;
        }

        public int Number { get; }

        public override string? SqlState => _sqlState;
    }
}
