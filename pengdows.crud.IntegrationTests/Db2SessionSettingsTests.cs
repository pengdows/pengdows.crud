using testbed.Db2;
using Xunit;

namespace pengdows.crud.IntegrationTests;

/// <summary>
/// Live regression coverage for <see cref="pengdows.crud.dialects.Db2Dialect.GetBaseSessionSettings"/>.
/// Confirms the distinction this project cares about: not just "our code emits SQL we believe is
/// valid Db2 syntax" but "the actual driver and actual server accept it." Verified once, live,
/// against Db2 LUW 11.5.8.0 (via a throwaway probe) that each of the three SET statements —
/// individually and batched as one semicolon-separated statement — executes successfully; this
/// test locks that in as a permanent regression check by exercising the real construction path
/// (session settings are applied automatically on first connection open, not called directly).
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
}
