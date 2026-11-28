using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Native round trip found a gap: Snowflake returns a VECTOR(FLOAT, n) as its bracketed text
/// ("[1.000000,2.000000,3.000000]"), and the library did not read that text into a float[]. The same text form
/// is read for TiDB by BracketedVectorCoercion; Snowflake's registration must include it.
/// </summary>
public sealed class SnowflakeVectorTextTests
{
    [Fact]
    public void SnowflakeVectorText_ReadsAsFloatArray()
    {
        var registry = new CoercionRegistry();
        SnowflakeDialect.CreateSnowflakeTraits().RegisterCoercions?.Invoke(registry);

        var read = registry.TryRead(new DbValue("[1.000000,2.000000,3.000000]"), typeof(float[]), out var value,
            SupportedDatabase.Snowflake);

        Assert.True(read);
        Assert.Equal(new[] { 1f, 2f, 3f }, (float[]?)value);
    }
}
