using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class ConnectionStringHelperAdditionalBranchTests
{
    [Fact]
    public void Create_WithMalformedInputThatCannotBeRawDataSource_ReturnsFallbackBuilder()
    {
        var preferred = new DbConnectionStringBuilder();

        var result = ConnectionStringHelper.Create(preferred, "\0");

        Assert.NotNull(result);
        Assert.IsType<DbConnectionStringBuilder>(result);
    }

    [Fact]
    public void TryApply_WithNullBuilder_ReturnsFalse()
    {
        var tryApply = typeof(ConnectionStringHelper)
            .GetMethod("TryApply", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TryApply method not found.");

        var applied = (bool)tryApply.Invoke(null, new object?[] { null, "x=y" })!;

        Assert.False(applied);
    }

    [Fact]
    public void TrySetRawDataSource_WhenBuilderThrows_ReturnsFalse()
    {
        var trySet = typeof(ConnectionStringHelper)
            .GetMethod("TrySetRawDataSource", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TrySetRawDataSource method not found.");

        var applied = (bool)trySet.Invoke(null, new object[] { new ThrowingIndexerBuilder(), "raw" })!;

        Assert.False(applied);
    }

    private sealed class ThrowingIndexerBuilder : DbConnectionStringBuilder
    {
        [AllowNull]
        public override object this[string keyword]
        {
            get => base[keyword];
            set => throw new InvalidOperationException("cannot set value");
        }
    }
}
