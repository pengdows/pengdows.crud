using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests
{
    // A provider setting that cannot be applied must say so. Silently skipping it leaves the parameter with the
    // wrong provider type, and the database then sees a different value than the one the caller asked for.
    public class ProviderPropertySetterNoSilentNoOpTests
    {
        private sealed class Target
        {
            public int Count { get; set; }
            public string? Label { get; set; }
            public int ReadOnlyValue { get; } = 1;
            public Uri? Unsupported { get; set; }
            public SampleEnum Mode { get; set; }
        }

        private enum SampleEnum
        {
            Alpha,
            Beta
        }

        [Fact]
        public void Set_MissingProperty_Throws()
        {
            var target = new Target();

            Assert.Throws<InvalidOperationException>(() => ProviderPropertySetter.Set(target, "NoSuchProperty", "x"));
        }

        [Fact]
        public void Set_ReadOnlyProperty_Throws()
        {
            var target = new Target();

            Assert.Throws<InvalidOperationException>(() => ProviderPropertySetter.Set(target, "ReadOnlyValue", "2"));
        }

        [Fact]
        public void Set_UnsupportedPropertyType_Throws()
        {
            var target = new Target();

            Assert.Throws<InvalidOperationException>(() =>
                ProviderPropertySetter.Set(target, nameof(Target.Unsupported), "http://example.test"));
        }

        [Fact]
        public void Set_UnparseableEnumText_Throws()
        {
            var target = new Target();

            Assert.Throws<InvalidOperationException>(() => ProviderPropertySetter.Set(target, nameof(Target.Mode), "Gamma"));
        }

        [Fact]
        public void TrySet_MissingProperty_ReturnsFalseAndChangesNothing()
        {
            var target = new Target();

            Assert.False(ProviderPropertySetter.TrySet(target, "NoSuchProperty", "x"));
            Assert.Equal(0, target.Count);
        }

        [Fact]
        public void TrySet_ReadOnlyProperty_StillThrows()
        {
            var target = new Target();

            Assert.Throws<InvalidOperationException>(() => ProviderPropertySetter.TrySet(target, "ReadOnlyValue", "2"));
        }

        [Fact]
        public void TrySet_UnparseableEnumText_StillThrows()
        {
            var target = new Target();

            Assert.Throws<InvalidOperationException>(() => ProviderPropertySetter.TrySet(target, nameof(Target.Mode), "Gamma"));
        }

        [Fact]
        public void TrySet_ValidValue_ReturnsTrueAndApplies()
        {
            var target = new Target();

            Assert.True(ProviderPropertySetter.TrySet(target, nameof(Target.Count), "9"));
            Assert.Equal(9, target.Count);
        }

        [Fact]
        public void Set_ValidValue_StillApplies()
        {
            var target = new Target();

            ProviderPropertySetter.Set(target, nameof(Target.Count), "7");
            ProviderPropertySetter.Set(target, nameof(Target.Label), "ok");
            ProviderPropertySetter.Set(target, nameof(Target.Mode), "Beta");

            Assert.Equal(7, target.Count);
            Assert.Equal("ok", target.Label);
            Assert.Equal(SampleEnum.Beta, target.Mode);
        }
    }
}
