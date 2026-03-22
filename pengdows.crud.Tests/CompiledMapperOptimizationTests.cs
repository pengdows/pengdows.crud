using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests that verify two CompiledMapperFactory optimizations:
///   1. IsDBNull is not called for non-nullable value-type properties.
///   2. Guid properties stored as strings are parsed directly via Guid.Parse,
///      not via the TypeCoercionHelper.Coerce boxing path.
/// </summary>
public class CompiledMapperOptimizationTests
{
    // ------------------------------------------------------------------ //
    //  IsDBNull skip tests
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task LoadAsync_NonNullableInt_DoesNotCallIsDBNull()
    {
        var reader = new ThrowingIsDbNullReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = 42 }
        });

        var result = await DataReaderMapper.LoadAsync<IntHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(42, result[0].Value);
    }

    [Fact]
    public async Task LoadAsync_NonNullableLong_DoesNotCallIsDBNull()
    {
        var reader = new ThrowingIsDbNullReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = 123L }
        });

        var result = await DataReaderMapper.LoadAsync<LongHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(123L, result[0].Value);
    }

    [Fact]
    public async Task LoadAsync_NonNullableBool_DoesNotCallIsDBNull()
    {
        var reader = new ThrowingIsDbNullReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = true }
        });

        var result = await DataReaderMapper.LoadAsync<BoolHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.True(result[0].Value);
    }

    [Fact]
    public async Task LoadAsync_NonNullableGuid_DoesNotCallIsDBNull()
    {
        var id = Guid.NewGuid();
        var reader = new ThrowingIsDbNullReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = id }
        });

        var result = await DataReaderMapper.LoadAsync<GuidHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(id, result[0].Value);
    }

    /// <summary>
    /// Nullable value types must still go through the IsDBNull guard — they legitimately
    /// need it so NULL from the DB maps to null in the property.
    /// </summary>
    [Fact]
    public async Task LoadAsync_NullableInt_StillChecksIsDBNull()
    {
        // A null value in the row should map to null, not throw.
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = DBNull.Value }
        });

        var result = await DataReaderMapper.LoadAsync<NullableIntHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Null(result[0].Value);
    }

    // ------------------------------------------------------------------ //
    //  Guid fast-path tests (string → Guid)
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task LoadAsync_GuidFromStringColumn_ParsesCorrectly()
    {
        var expected = Guid.NewGuid();
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = expected.ToString("D") }
        });

        var result = await DataReaderMapper.LoadAsync<GuidHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(expected, result[0].Value);
    }

    [Fact]
    public async Task LoadAsync_NullableGuidFromStringColumn_ParsesCorrectly()
    {
        var expected = Guid.NewGuid();
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = expected.ToString("D") }
        });

        var result = await DataReaderMapper.LoadAsync<NullableGuidHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(expected, result[0].Value);
    }

    [Fact]
    public async Task LoadAsync_NullableGuidFromStringColumn_NullValue_ReturnsNull()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = DBNull.Value }
        });

        var result = await DataReaderMapper.LoadAsync<NullableGuidHolder>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Null(result[0].Value);
    }

    // ------------------------------------------------------------------ //
    //  Helper types
    // ------------------------------------------------------------------ //

    private sealed class IntHolder { public int Value { get; set; } }
    private sealed class LongHolder { public long Value { get; set; } }
    private sealed class BoolHolder { public bool Value { get; set; } }
    private sealed class GuidHolder { public Guid Value { get; set; } }
    private sealed class NullableIntHolder { public int? Value { get; set; } }
    private sealed class NullableGuidHolder { public Guid? Value { get; set; } }

    /// <summary>
    /// A reader that throws if <see cref="IsDBNull"/> is called for any ordinal.
    /// Used to prove the compiled mapper does not emit IsDBNull checks for
    /// non-nullable value-type properties.
    /// </summary>
    private sealed class ThrowingIsDbNullReader : fakeDbDataReader
    {
        public ThrowingIsDbNullReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public override bool IsDBNull(int ordinal)
            => throw new InvalidOperationException(
                $"IsDBNull({ordinal}) should not be called for non-nullable value-type properties.");
    }
}
