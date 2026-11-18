using System.Data;
using System.IO;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.LargeObjects
{
    public class LargeObjectParameterTests
    {
        [Fact]
        public void TryMaterialize_Stream_BecomesBinary()
        {
            var parameter = new fakeDbParameter();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            Assert.True(LargeObjectParameter.TryMaterialize(parameter, stream));

            Assert.Equal(DbType.Binary, parameter.DbType);
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])parameter.Value);
            Assert.Equal(3, parameter.Size);
        }

        [Fact]
        public void TryMaterialize_TextReader_BecomesString()
        {
            var parameter = new fakeDbParameter();
            using var reader = new StringReader("hello");

            Assert.True(LargeObjectParameter.TryMaterialize(parameter, reader));

            Assert.Equal(DbType.String, parameter.DbType);
            Assert.Equal("hello", parameter.Value);
            Assert.Equal(5, parameter.Size);
        }

        [Fact]
        public void TryMaterialize_AnyOtherValue_ReturnsFalse()
        {
            var parameter = new fakeDbParameter();

            Assert.False(LargeObjectParameter.TryMaterialize(parameter, 42));
            Assert.False(LargeObjectParameter.TryMaterialize(parameter, null));
        }
    }
}
