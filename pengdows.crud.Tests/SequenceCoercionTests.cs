#pragma warning disable CS0618 // TypeCoercionOptions is internal plumbing here
using System;
using System.Collections.Generic;
using pengdows.crud;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// Collections are converted element by element in one place: an array or list from another collection, from numeric JSON
    /// text, or from a provider's vector value (its array, unwrapped first).
    /// </summary>
    public class SequenceCoercionTests
    {
        [Fact]
        public void AnArrayConvertsElementwiseToTheTargetType()
        {
            Assert.True(SequenceCoercion.TryCoerceSequence(new object[] { 1, 2, 3 }, typeof(long[]), TypeCoercionOptions.Default, out var result));

            Assert.Equal(new long[] { 1, 2, 3 }, result);
        }

        [Fact]
        public void ANumericJsonArrayTextReadsAsItsElements()
        {
            Assert.True(SequenceCoercion.TryCoerceSequence("[1,2,3]", typeof(int[]), TypeCoercionOptions.Default, out var result));

            Assert.Equal(new[] { 1, 2, 3 }, result);
        }

        [Fact]
        public void ACollectionIsBuiltAsAListWhenTheTargetIsAList()
        {
            Assert.True(SequenceCoercion.TryCoerceSequence(new[] { 4, 5 }, typeof(List<int>), TypeCoercionOptions.Default, out var result));

            Assert.Equal(new List<int> { 4, 5 }, result);
        }

        [Fact]
        public void ANonCollectionIsNotASequence()
        {
            Assert.False(SequenceCoercion.TryCoerceSequence(42, typeof(int[]), TypeCoercionOptions.Default, out _));
        }
    }
}
