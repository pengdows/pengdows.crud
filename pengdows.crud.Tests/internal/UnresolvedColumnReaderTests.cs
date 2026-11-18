using System;
using System.Collections.Generic;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.@internal
{
    public class UnresolvedColumnReaderTests
    {
        private static fakeDbDataReader OneRow(object? value)
        {
            var reader = new fakeDbDataReader(new List<Dictionary<string, object>>
            {
                new() { ["v"] = value ?? DBNull.Value }
            });
            Assert.True(reader.Read());
            return reader;
        }

        [Fact]
        public void Read_NullColumn_ReturnsDBNull()
        {
            Assert.Same(DBNull.Value, UnresolvedColumnReader.Read(OneRow(null), 0, typeof(int)));
        }

        [Fact]
        public void Read_TypeWithNoReader_ThrowsNotSupported()
        {
            var ex = Assert.Throws<NotSupportedException>(() =>
                UnresolvedColumnReader.Read(OneRow(42), 0, typeof(int)));

            Assert.Contains("Int32", ex.Message, StringComparison.Ordinal);
        }
    }
}
