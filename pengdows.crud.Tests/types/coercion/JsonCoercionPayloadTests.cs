using System;
using System.IO;
using System.Text;
using System.Text.Json;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.JsonPayloads
{
    public class JsonCoercionPayloadTests
    {
        [Fact]
        public void CoerceJsonValue_ValueAlreadyOfTheTargetType_PassesThrough()
        {
            Assert.Equal("kept", JsonCoercion.CoerceJsonValue("kept", typeof(object), TypeCoercionOptions.Default));
        }

        [Fact]
        public void CoerceJsonValue_TextToReadOnlyMemory_CopiesTheText()
        {
            var result = JsonCoercion.CoerceJsonValue("{\"a\":1}", typeof(ReadOnlyMemory<char>), TypeCoercionOptions.Default);

            Assert.Equal("{\"a\":1}", new string(((ReadOnlyMemory<char>)result!).Span));
        }

        [Fact]
        public void CoerceJsonValue_BlankTextToNullableTarget_ReadsJsonNull()
        {
            Assert.Null(JsonCoercion.CoerceJsonValue("   ", typeof(int?), TypeCoercionOptions.Default));
        }

        [Fact]
        public void CoerceJsonValue_EmptyStreamToStructuredTarget_ThrowsJsonException()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("   "));

            var ex = Assert.Throws<JsonException>(() =>
                JsonCoercion.CoerceJsonValue(stream, typeof(System.Collections.Generic.Dictionary<string, int>),
                    TypeCoercionOptions.Default));

            Assert.Contains("cannot be empty", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void CoerceJsonValue_MalformedTextToJsonDocument_ThrowsWithTheTargetNamed()
        {
            var ex = Assert.Throws<JsonException>(() =>
                JsonCoercion.CoerceJsonValue("{", typeof(JsonDocument), TypeCoercionOptions.Default));

            Assert.Contains("JsonDocument", ex.Message, StringComparison.Ordinal);
            Assert.NotNull(ex.InnerException);
        }
    }
}
