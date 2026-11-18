using System.Collections.Generic;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TextCollections
{
    public class TextCollectionCoercionsTests
    {
        [Fact]
        public void ListOfInt_NullValue_ReturnsFalse()
        {
            Assert.False(new ListOfIntCoercion().TryRead(new DbValue(null), out var value));
            Assert.Null(value);
        }

        [Fact]
        public void ListOfInt_ListValue_PassesThrough()
        {
            var list = new List<int> { 1, 2 };

            Assert.True(new ListOfIntCoercion().TryRead(new DbValue(list), out var value));
            Assert.Same(list, value);
        }

        [Fact]
        public void ListOfInt_UnsupportedValue_ReturnsFalse()
        {
            Assert.False(new ListOfIntCoercion().TryRead(new DbValue(42), out var value));
            Assert.Null(value);
        }

        [Fact]
        public void JsonObject_NullValue_ReturnsFalse()
        {
            Assert.False(new JsonObjectCoercion().TryRead(new DbValue(null), out var value));
            Assert.Null(value);
        }

        [Fact]
        public void JsonObject_DictionaryValue_PassesThrough()
        {
            var map = new Dictionary<string, object> { ["k"] = "v" };

            Assert.True(new JsonObjectCoercion().TryRead(new DbValue(map), out var value));
            Assert.Same(map, value);
        }

        [Fact]
        public void JsonObject_JsonText_MapsEachValueKind()
        {
            const string json = "{\"s\":\"x\",\"n\":1,\"f\":1.5,\"t\":true,\"f2\":false,\"z\":null,\"o\":{\"a\":1}}";

            Assert.True(new JsonObjectCoercion().TryRead(new DbValue(json), out var value));

            Assert.Equal("x", value!["s"]);
            // Known: ToValue's conditional promotes an integer to double, so 1 comes back as 1.0. Pinned here as the
            // current behavior; see the report on integer precision above 2^53.
            Assert.Equal(1.0, value["n"]);
            Assert.Equal(1.5, value["f"]);
            Assert.Equal(true, value["t"]);
            Assert.Equal(false, value["f2"]);
            Assert.Null(value["z"]);
            Assert.Equal("{\"a\":1}", value["o"]);
        }

        [Fact]
        public void JsonObject_UnsupportedValue_ReturnsFalse()
        {
            Assert.False(new JsonObjectCoercion().TryRead(new DbValue(42), out var value));
            Assert.Null(value);
        }

        [Fact]
        public void RangeArray_NullValue_ReturnsFalse()
        {
            Assert.False(new RangeArrayCoercion().TryRead(new DbValue(null), out var value));
            Assert.Null(value);
        }

        [Fact]
        public void RangeArray_OneInvalidRange_ReturnsFalse()
        {
            Assert.False(new RangeArrayCoercion().TryRead(new DbValue("[1,5);garbage"), out var value));
            Assert.Null(value);
        }

        [Fact]
        public void RangeArray_UnsupportedValue_ReturnsFalse()
        {
            Assert.False(new RangeArrayCoercion().TryRead(new DbValue(42), out var value));
            Assert.Null(value);
        }
    }
}
