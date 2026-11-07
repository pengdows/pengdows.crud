using System;
using System.Collections.Generic;
using System.Text;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// Binary column values are read in one place: the bytes of a column, and a GUID from its 16 bytes in the dialect's byte
    /// order (the mixed-endian .NET order, or big-endian). A value that is not 16 bytes is refused.
    /// </summary>
    public class BinaryCoercionTests
    {
        private static fakeDbDataReader Row(object value)
        {
            var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["v"] = value } });
            Assert.True(reader.Read());
            return reader;
        }

        [Fact]
        public void ABinaryColumnReadsAsItsBytes()
        {
            var data = Encoding.UTF8.GetBytes("abc");

            Assert.Equal(data, BinaryCoercion.ReadBytes(Row(data), 0));
        }

        [Fact]
        public void A16ByteColumnReadsAsAGuidInTheRequestedByteOrder()
        {
            var guid = Guid.Parse("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9");
            var mixed = guid.ToByteArray();
            var big = guid.ToByteArray(bigEndian: true);

            Assert.Equal(guid, BinaryCoercion.ReadGuidFromBytes(Row(mixed), 0, false));
            Assert.Equal(guid, BinaryCoercion.ReadGuidFromBytes(Row(big), 0, true));
        }

        [Fact]
        public void AValueThatIsNot16BytesIsRefusedAsAGuid()
        {
            Assert.Throws<InvalidValueException>(() => BinaryCoercion.ReadGuidFromBytes(Row(new byte[15]), 0));
            Assert.Throws<InvalidValueException>(() => BinaryCoercion.GuidFromBinary(new byte[17], false));
        }

        [Fact]
        public void AGuidRoundTripsThroughItsBytesInEitherOrder()
        {
            var guid = Guid.NewGuid();

            Assert.Equal(guid, BinaryCoercion.GuidFromBinary(BinaryCoercion.GuidBytes(guid, false), false));
            Assert.Equal(guid, BinaryCoercion.GuidFromBinary(BinaryCoercion.GuidBytes(guid, true), true));
        }
    }
}
