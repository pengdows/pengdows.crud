using System;
using pengdows.crud.dialects;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.OffsetParameters
{
    public class OffsetParameterValuesTests
    {
        [Fact]
        public void TryGet_RememberedParameter_ReturnsTheValue()
        {
            var parameter = new fakeDbParameter();
            var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

            OffsetParameterValues.Remember(parameter, when);

            Assert.True(OffsetParameterValues.TryGet(parameter, out var value));
            Assert.Equal(when, value);
        }

        [Fact]
        public void TryGet_NeverRememberedParameter_ReturnsFalse()
        {
            Assert.False(OffsetParameterValues.TryGet(new fakeDbParameter(), out var value));
            Assert.Equal(default, value);
        }
    }
}
