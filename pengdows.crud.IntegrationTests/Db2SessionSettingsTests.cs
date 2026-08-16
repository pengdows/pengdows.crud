using IBM.Data.Db2;
using pengdows.crud;
using testbed.Db2;
using Xunit;

namespace pengdows.crud.IntegrationTests;

/// <summary>
/// Live regression coverage for <see cref="pengdows.crud.dialects.Db2Dialect.GetBaseSessionSettings"/>,
/// at two levels: first, that the driver and server accept the SQL at all (not just that our code
/// believes it's valid syntax); second — the invariant that actually matters —
/// <see cref="PooledConnectionReuse_CleansContaminatedIsolationAndTemporalRegisters"/> proves a
/// dirty pooled Db2 session is actually cleaned before the next pengdows operation sees it, not
/// merely that initialization doesn't throw. That second test was confirmed to genuinely fail
/// (contamination leaked through: "UR" instead of the reset value) when
/// <c>GetBaseSessionSettings()</c> was temporarily stubbed to return an empty string, then
/// confirmed to pass once restored — proving it detects the regression it guards against.
/// </summary>
public class Db2SessionSettingsTests
{
    [Fact]
    public async Task Connection_AppliesBaseSessionSettings_WithoutError()
    {
        await using var container = new Db2TestContainer();
        await container.StartAsync();

        await using var context = await container.GetDatabaseContextAsync(null!);

        // GetBaseSessionSettings() (CURRENT ISOLATION RESET + both temporal registers reset to
        // NULL) runs automatically on first connection open via ExecuteSessionSettings. A plain
        // round trip proves the batched statement doesn't break real connection acquisition.
        await using var sc = context.CreateSqlContainer("SELECT 1 FROM SYSIBM.SYSDUMMY1");
        var result = await sc.ExecuteScalarRequiredAsync<int>();

        Assert.Equal(1, result);
    }

    /// <summary>
    /// The invariant that actually matters: not "Db2 accepts our session-init SQL" (covered above)
    /// but "a dirty pooled Db2 session is actually cleaned before the next pengdows operation."
    /// Forces a single physical connection (Max Pool Size=1) so reuse across logical checkouts is
    /// guaranteed rather than merely likely, deliberately contaminates CURRENT ISOLATION and
    /// CURRENT TEMPORAL SYSTEM_TIME, returns the connection to the pool, then reacquires it and
    /// confirms both registers are back to baseline — proving pengdows prevents session-state
    /// leakage through the Db2 connection pool, not just that initialization doesn't throw.
    /// </summary>
    [Fact]
    public async Task PooledConnectionReuse_CleansContaminatedIsolationAndTemporalRegisters()
    {
        await using var container = new Db2TestContainer();
        await container.StartAsync();

        var pooledConnectionString = container.ConnectionString + "Max Pool Size=1;Min Pool Size=1;";
        await using var context = new DatabaseContext(pooledConnectionString, DB2Factory.Instance);

        // Contaminate: leave both special registers non-default before the connection returns to
        // the pool — exactly what a prior borrower could leave behind.
        await using (var contaminate = context.CreateSqlContainer(
                         "SET CURRENT ISOLATION UR; SET CURRENT TEMPORAL SYSTEM_TIME = CURRENT TIMESTAMP;"))
        {
            await contaminate.ExecuteNonQueryAsync();
        }

        // Reacquire — with Max Pool Size=1 this MUST be the same physical connection. If pengdows
        // did not reapply GetBaseSessionSettings() on this checkout, the contaminated value ("UR")
        // would still be in effect here. Verified live that SET CURRENT ISOLATION RESET does not
        // make the register read back as "CS" (the documented package default) — RESET clears the
        // register to "no override," which reads as an empty string, not the resolved isolation
        // name. The important assertion either way is that it's no longer the contaminated value.
        await using (var isolationCheck = context.CreateSqlContainer("VALUES CURRENT ISOLATION"))
        {
            var isolation = await isolationCheck.ExecuteScalarRequiredAsync<string>();
            Assert.Equal(string.Empty, isolation.Trim());
        }

        await using (var temporalCheck = context.CreateSqlContainer(
                         "VALUES (CASE WHEN CURRENT TEMPORAL SYSTEM_TIME IS NULL THEN 'NULL' ELSE 'SET' END)"))
        {
            var temporal = await temporalCheck.ExecuteScalarRequiredAsync<string>();
            Assert.Equal("NULL", temporal.Trim());
        }
    }
}
