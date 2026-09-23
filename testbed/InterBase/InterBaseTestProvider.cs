using pengdows.crud;

namespace testbed.InterBase;

/// <summary>
/// Overrides <see cref="CreateTable"/> for the one genuine InterBase-specific DDL need identified
/// live: <see cref="InterBaseDialect"/>'s <see cref="GeneratedKeyPlan.PrefetchSequence"/> plan
/// (see its class remarks) requires a <c>CREATE GENERATOR</c> to already exist, named
/// <c>{tableName}_seq</c> per <c>TableGateway.Core.cs</c>'s fixed <c>GetSequenceName()</c>
/// convention — CONFIRMED live: the base class's generic <c>CreateTable</c> has no generator/
/// sequence step at all (every other generated-key mechanism this testbed exercises needs none),
/// so the very first insert against a freshly created table fails with
/// "generator test_table_seq is not defined" until this is added.
/// </summary>
public class InterBaseTestProvider : TestProvider
{
    public InterBaseTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public override async Task CreateTable()
    {
        // The base implementation's own DROP uses "DROP TABLE IF EXISTS" — CONFIRMED live that
        // InterBase rejects the "IF EXISTS" clause outright (SupportsDropTableIfExists is false),
        // so that statement always fails and is silently swallowed by the base method's own
        // try/catch, never actually dropping a table left over from a prior run against this
        // persistent (non-ephemeral, see InterBaseTestContainer's class remarks) database. Drop it
        // here first, with InterBase's real bare syntax, before base.CreateTable() runs its own
        // (harmlessly failing) IF-EXISTS attempt and then CREATE TABLE.
        var predrop = _context.CreateSqlContainer();
        predrop.Query.Append($"DROP TABLE {_context.WrapObjectName("test_table")}");
        try
        {
            await predrop.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist yet — expected on a fresh database.
        }

        await base.CreateTable();

        // Must be quoted to match InterBaseDialect.GetSequenceNextValQuery's own
        // WrapObjectName(sequenceName) call — a bare, unquoted CREATE GENERATOR here would fold
        // to uppercase (InterBase's confirmed unquoted-identifier behavior), while the dialect's
        // quoted GEN_ID("test_table_seq", 1) looks up the exact-case, case-sensitive quoted name.
        // CONFIRMED live: this exact mismatch was the cause of a
        // "generator test_table_seq is not defined" failure on the first version of this method.
        var generatorName = _context.WrapObjectName("test_table_seq");
        var sc = _context.CreateSqlContainer();
        sc.Query.Append($"DROP GENERATOR {generatorName}");
        try
        {
            await sc.ExecuteNonQueryAsync();
        }
        catch
        {
            // Generator did not exist yet — expected on a fresh database.
        }

        sc.Clear();
        sc.Query.Append($"CREATE GENERATOR {generatorName}");
        await sc.ExecuteNonQueryAsync();
    }

}
