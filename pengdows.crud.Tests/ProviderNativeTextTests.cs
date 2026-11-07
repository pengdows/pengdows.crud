using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// A driver's own value for a native column (Npgsql's NpgsqlPoint, NpgsqlTsQuery, ...) has no conversion to text, so its
    /// text form is taken from the value itself. The recognised namespace is kept in one registry.
    /// </summary>
    public class ProviderNativeTextTests
    {
        [Fact]
        public void ANpgsqlNativeValueHasItsTextForm()
        {
            var value = new NpgsqlTypes.FakeNativeText();

            Assert.True(ProviderNativeText.TryText(value, out var text));
            Assert.Equal("(1,2)", text);
        }

        [Fact]
        public void AnOrdinaryValueHasNoProviderNativeText()
        {
            Assert.False(ProviderNativeText.TryText(42, out _));
            Assert.False(ProviderNativeText.TryText("text", out _));
        }
    }
}
