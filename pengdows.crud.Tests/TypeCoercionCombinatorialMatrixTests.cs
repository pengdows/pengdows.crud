using System;
using System.Collections.Generic;
using Xunit;

namespace pengdows.crud.Tests;

// FEAT-009: TypeCoercionHelper has 8 existing test files (~2100 lines) covering many individual
// scenarios, but none of them exercise a systematic source-CLR-type × target-CLR-type matrix the
// way DbTypeProviderMatrixTests.cs does for dialect parameter construction. This file adds that
// missing combinatorial dimension for TypeCoercionHelper.Coerce(value, sourceType, targetType) —
// numeric widening/narrowing, string↔numeric, string↔bool, bool↔numeric, and Guid↔string —
// deliberately excluding DateTime/DateTimeOffset, which already have extensive dedicated,
// policy-aware coverage elsewhere (TypeCoercionHelperExtensiveTests.cs and siblings) that this
// file would only duplicate.
public sealed class TypeCoercionCombinatorialMatrixTests
{
    public static IEnumerable<object[]> SourceValueTargetTypeAndExpected()
    {
        // Numeric widening
        yield return [(byte)5, typeof(int), 5];
        yield return [(byte)5, typeof(long), 5L];
        yield return [(short)5, typeof(int), 5];
        yield return [(short)5, typeof(double), 5d];
        yield return [5, typeof(long), 5L];
        yield return [5, typeof(double), 5d];
        yield return [5, typeof(decimal), 5m];
        yield return [5, typeof(float), 5f];
        yield return [5f, typeof(double), 5d];
        yield return [5L, typeof(decimal), 5m];

        // Numeric narrowing (in-range, exact whole values — no rounding ambiguity)
        yield return [5L, typeof(int), 5];
        yield return [5L, typeof(short), (short)5];
        yield return [5.0d, typeof(int), 5];
        yield return [5.0d, typeof(float), 5f];
        yield return [5m, typeof(int), 5];
        yield return [5m, typeof(double), 5d];

        // String <-> numeric
        yield return ["42", typeof(int), 42];
        yield return ["42", typeof(long), 42L];
        yield return ["42", typeof(short), (short)42];
        yield return ["42", typeof(byte), (byte)42];
        yield return ["42.5", typeof(double), 42.5d];
        yield return ["42.5", typeof(decimal), 42.5m];
        yield return ["42.5", typeof(float), 42.5f];
        yield return [42, typeof(string), "42"];
        yield return [42L, typeof(string), "42"];
        yield return [42.5d, typeof(string), "42.5"];
        yield return [42.5m, typeof(string), "42.5"];

        // String <-> bool
        yield return ["true", typeof(bool), true];
        yield return ["false", typeof(bool), false];
        yield return ["True", typeof(bool), true];
        yield return [true, typeof(string), "True"];
        yield return [false, typeof(string), "False"];

        // bool <-> numeric
        yield return [true, typeof(int), 1];
        yield return [false, typeof(int), 0];
        yield return [(byte)1, typeof(bool), true];
        yield return [(byte)0, typeof(bool), false];

        // Guid -> string is deliberately NOT in this matrix — see
        // Coerce_GuidToString_ThrowsInvalidCastException below, a real asymmetry this matrix
        // found: string -> Guid succeeds (case below), but Guid -> string does not.
        var guid = Guid.NewGuid();
        yield return [guid.ToString(), typeof(Guid), guid];
    }

    [Theory]
    [MemberData(nameof(SourceValueTargetTypeAndExpected))]
    public void Coerce_AcrossSourceAndTargetTypeCombinations_ProducesExpectedValue(
        object sourceValue, Type targetType, object expected)
    {
        var result = TypeCoercionHelper.Coerce(sourceValue, sourceValue.GetType(), targetType);

        Assert.NotNull(result);
        Assert.IsType(targetType, result);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Coerce_GuidToString_ThrowsInvalidCastException()
    {
        // Found by this matrix, not previously characterized anywhere: TypeCoercionHelper.Coerce's
        // generic (value, sourceType, targetType) overload supports string -> Guid (case above)
        // but NOT the reverse. Guid doesn't implement IConvertible, and CoerceCore's registry/
        // advanced-converter paths apparently don't special-case Guid -> string either, so it
        // falls all the way to ConvertWithCache's Convert.ChangeType-based fallback, which throws.
        // This does not necessarily affect normal entity Guid columns, which go through dialect-
        // specific storage-format handling (see docs — PassThrough/String/Binary per dialect), not
        // this generic helper — but it's a real, surprising gap in this specific public API that's
        // worth locking down rather than leaving uncharacterized.
        var guid = Guid.NewGuid();

        var ex = Assert.Throws<InvalidCastException>(
            () => TypeCoercionHelper.Coerce(guid, typeof(Guid), typeof(string)));

        Assert.Contains("Guid", ex.Message);
        Assert.Contains("String", ex.Message);
    }
}
