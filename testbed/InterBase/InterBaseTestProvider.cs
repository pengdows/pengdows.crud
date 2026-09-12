using System.Data;
using pengdows.crud;
using pengdows.crud.exceptions;

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

    /// <summary>
    /// Overrides the base class's duplicate-PK check with a raw-SQL insert instead of
    /// <c>TableGateway.CreateAsync</c>. CONFIRMED live that the base version cannot actually force
    /// a PK collision here: <see cref="InterBaseDialect"/>'s
    /// <see cref="GeneratedKeyPlan.PrefetchSequence"/> plan makes <c>CreateAsync</c> fetch a fresh
    /// generator value and overwrite the entity's (client-supplied, writable) <c>Id</c> on EVERY
    /// call — including the base test's second call reusing the same entity object — so the two
    /// inserts silently land under two different, non-colliding ids and no exception is ever
    /// thrown ("[ErrorMapping] Expected DatabaseException for duplicate PK — none thrown"). A raw
    /// INSERT with an explicit, repeated id bypasses the generator entirely and forces a genuine
    /// PK violation, which is what this test exists to exercise — the rest of the base method
    /// (syntax-error mapping) is unaffected by this issue and does not need overriding.
    /// </summary>
    protected override async Task TestErrorMapping()
    {
        var id = DateTime.UtcNow.Ticks % 1_000_000_000;

        async Task InsertRaw()
        {
            var insert = _context.CreateSqlContainer();
            insert.Query.Append(
                $"INSERT INTO {_context.WrapObjectName("test_table")} " +
                $"({_context.WrapObjectName("id")}, {_context.WrapObjectName("name")}, " +
                $"{_context.WrapObjectName("description")}, {_context.WrapObjectName("value")}, " +
                $"{_context.WrapObjectName("is_active")}, {_context.WrapObjectName("created_at")}, " +
                $"{_context.WrapObjectName("created_by")}, {_context.WrapObjectName("updated_at")}, " +
                $"{_context.WrapObjectName("updated_by")}) VALUES " +
                $"({insert.MakeParameterName("p0")}, {insert.MakeParameterName("p1")}, " +
                $"{insert.MakeParameterName("p2")}, {insert.MakeParameterName("p3")}, " +
                $"{insert.MakeParameterName("p4")}, {insert.MakeParameterName("p5")}, " +
                $"{insert.MakeParameterName("p6")}, {insert.MakeParameterName("p7")}, " +
                $"{insert.MakeParameterName("p8")})");
            insert.AddParameterWithValue("p0", DbType.Int64, id);
            insert.AddParameterWithValue("p1", DbType.String, "Test");
            insert.AddParameterWithValue("p2", DbType.String, "error-mapping-test");
            insert.AddParameterWithValue("p3", DbType.Int32, 0);
            insert.AddParameterWithValue("p4", DbType.Boolean, true);
            insert.AddParameterWithValue("p5", DbType.DateTime, DateTime.UtcNow);
            insert.AddParameterWithValue("p6", DbType.String, "testbed");
            insert.AddParameterWithValue("p7", DbType.DateTime, DateTime.UtcNow);
            insert.AddParameterWithValue("p8", DbType.String, "testbed");
            await insert.ExecuteNonQueryAsync();
        }

        await InsertRaw();

        try
        {
            await InsertRaw();
            throw new Exception("[ErrorMapping] Expected DatabaseException for duplicate PK — none thrown");
        }
        catch (DatabaseException ex)
        {
            CheckOk("ErrorMapping.UniqueViolation", $"  [ErrorMapping] Unique violation → DatabaseException: OK ({ex.Message[..Math.Min(80, ex.Message.Length)]}...)");
        }
        finally
        {
            await CleanupTestRow(id);
        }

        var healthCount = await CountTestRows();
        CheckOk("ErrorMapping.ConnectionHealth", $"  [ErrorMapping] Connection health after exception: OK (count={healthCount})");

        var badSc = _context.CreateSqlContainer("SELECT * FROM");
        DatabaseException? syntaxEx = null;
        try
        {
            await badSc.ExecuteNonQueryAsync();
            throw new Exception("[ErrorMapping] Expected DatabaseException for syntax error — none thrown");
        }
        catch (DatabaseException ex)
        {
            syntaxEx = ex;
        }

        if (string.IsNullOrWhiteSpace(syntaxEx?.Message))
            throw new Exception("[ErrorMapping] Syntax error exception had empty message");

        CheckOk("ErrorMapping.SyntaxError", $"  [ErrorMapping] Syntax error → DatabaseException: OK ({syntaxEx.Message[..Math.Min(80, syntaxEx.Message.Length)]}...)");
    }
}
