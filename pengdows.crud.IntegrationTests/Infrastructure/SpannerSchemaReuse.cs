using System.Collections.Concurrent;
using System.Text;
using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Spanner applies every DDL statement as a schema change that takes seconds (measured: DROP TABLE
/// about 3.6 s, CREATE TABLE about 2 s, against well under 0.1 s for TRUNCATE), and the per-test reset
/// dropped and recreated each test class's tables before every test: about 40 of Spanner's 58 minutes.
/// A test class sets up the same schema for every test, so after its first test the tables are only
/// emptied, with one TRUNCATE of every table (Spanner refuses to truncate a parent alone while a
/// child row references it, but accepts them together). The catalog must still match what the
/// class's setup left, or the test gets the full reset: a test that altered, dropped or added a
/// table can't leak a different schema into the next one.
/// </summary>
internal static class SpannerSchemaReuse
{
    private sealed record Owner(Type TestClass, string Fingerprint);

    // By connection string: one Spanner database per fixture.
    private static readonly ConcurrentDictionary<string, Owner> Owners = new(StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="testClass"/> set this database up for its previous test and the
    /// schema is unchanged, after emptying every table; the caller then skips cleanup and setup.
    /// </summary>
    public static async Task<bool> TryReuseAsync(IDatabaseContext context, Type testClass)
    {
        if (context.Product != SupportedDatabase.Spanner ||
            !Owners.TryGetValue(context.ConnectionString, out var owner) || owner.TestClass != testClass)
        {
            return false;
        }

        var (fingerprint, tables) = await ReadCatalogAsync(context);
        if (!string.Equals(fingerprint, owner.Fingerprint, StringComparison.Ordinal))
        {
            Owners.TryRemove(context.ConnectionString, out _);
            return false;
        }

        if (tables.Count > 0)
        {
            await using var truncate = context.CreateSqlContainer(
                "TRUNCATE TABLE " + string.Join(", ", tables.Select(context.WrapObjectName)));
            await truncate.ExecuteNonQueryAsync();
        }

        return true;
    }

    /// <summary>Records the schema <paramref name="testClass"/>'s setup just left.</summary>
    public static async Task RecordAsync(IDatabaseContext context, Type testClass)
    {
        if (context.Product != SupportedDatabase.Spanner)
        {
            return;
        }

        var (fingerprint, _) = await ReadCatalogAsync(context);
        Owners[context.ConnectionString] = new Owner(testClass, fingerprint);
    }

    /// <summary>Forgets the recorded schema: the next test resets in full.</summary>
    public static void Forget(IDatabaseContext context) => Owners.TryRemove(context.ConnectionString, out _);

    // Every user table's columns and indexes, from the catalog (a query, not DDL).
    private static async Task<(string Fingerprint, IReadOnlyList<string> Tables)> ReadCatalogAsync(
        IDatabaseContext context)
    {
        var text = new StringBuilder();
        var tables = new List<string>();
        await using (var columns = context.CreateSqlContainer(
                         "SELECT table_name, column_name, data_type, is_nullable, column_default " +
                         "FROM information_schema.columns WHERE table_schema = 'public' " +
                         "ORDER BY table_name, ordinal_position"))
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                if (tables.Count == 0 || tables[^1] != table)
                {
                    tables.Add(table);
                }

                for (var i = 0; i < 5; i++)
                {
                    text.Append(reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString()).Append('|');
                }

                text.Append('\n');
            }
        }

        await using (var indexes = context.CreateSqlContainer(
                         "SELECT table_name, index_name, index_type FROM information_schema.indexes " +
                         "WHERE table_schema = 'public' ORDER BY table_name, index_name"))
        await using (var reader = await indexes.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                text.Append("index|").Append(reader.GetString(0)).Append('|').Append(reader.GetString(1))
                    .Append('|').Append(reader.IsDBNull(2) ? "<null>" : reader.GetValue(2).ToString()).Append('\n');
            }
        }

        return (text.ToString(), tables);
    }
}
