using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// The provider vector values the library unwraps to arrays (SqlClient's SqlVector&lt;T&gt;, the Pgvector.Npgsql Vector) are
    /// recognised in one registry, not in the general coercion path.
    /// </summary>
    public class ProviderVectorValuesTests
    {
        [Fact]
        public void APgvectorIsUnwrappedToItsElements()
        {
            var vector = new Pgvector.Vector(new[] { 1f, 2f });

            Assert.True(ProviderVectorValues.TryUnwrap(vector, out var array));
            Assert.Equal(new[] { 1f, 2f }, Assert.IsType<float[]>(array));
        }

        [Fact]
        public void AnSqlVectorIsUnwrappedToItsMemory()
        {
            var vector = new SqlVector<float>(new[] { 3f });

            Assert.True(ProviderVectorValues.TryUnwrap(vector, out var array));
            Assert.Equal(new[] { 3f }, Assert.IsType<float[]>(array));
        }

        [Fact]
        public void AnOrdinaryValueIsNotAProviderVector()
        {
            Assert.False(ProviderVectorValues.TryUnwrap(new[] { 1f }, out _));
            Assert.False(ProviderVectorValues.TryUnwrap("1,2", out _));
        }

        // Stands in for Microsoft.Data.SqlTypes.SqlVector<T>, recognised by its generic name.
        private sealed class SqlVector<T> where T : unmanaged
        {
            public SqlVector(T[] values) => Memory = values;
            public ReadOnlyMemory<T> Memory { get; }
        }
    }
}
