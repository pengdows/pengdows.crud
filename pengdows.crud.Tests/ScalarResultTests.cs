using System;
using Xunit;

namespace pengdows.crud.Tests;

public class ScalarResultTests
{
    [Fact]
    public void HasValue_IsTrueOnlyForValueStatus()
    {
        var none = new ScalarResult<int>(ScalarStatus.None, default);
        var nullResult = new ScalarResult<int>(ScalarStatus.Null, default);
        var value = new ScalarResult<int>(ScalarStatus.Value, 42);

        Assert.False(none.HasValue);
        Assert.False(nullResult.HasValue);
        Assert.True(value.HasValue);
    }

    [Fact]
    public void Required_ReturnsValue_WhenStatusIsValue()
    {
        var result = new ScalarResult<int>(ScalarStatus.Value, 99);
        Assert.Equal(99, result.Required);
    }

    [Fact]
    public void Required_ThrowsWithNoRowsMessage_WhenStatusIsNone()
    {
        var result = new ScalarResult<string>(ScalarStatus.None, default);
        var ex = Assert.Throws<InvalidOperationException>(() => result.Required);
        Assert.Equal("Query returned no rows.", ex.Message);
    }

    [Fact]
    public void Required_ThrowsWithNullValueMessage_WhenStatusIsNull()
    {
        var result = new ScalarResult<string>(ScalarStatus.Null, default);
        var ex = Assert.Throws<InvalidOperationException>(() => result.Required);
        Assert.Equal("Query returned a null value.", ex.Message);
    }

    [Fact]
    public void RecordEquality_SameStatusAndValue_AreEqual()
    {
        var a = new ScalarResult<int>(ScalarStatus.Value, 42);
        var b = new ScalarResult<int>(ScalarStatus.Value, 42);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void RecordEquality_DifferentStatus_AreNotEqual()
    {
        var a = new ScalarResult<int>(ScalarStatus.Value, 42);
        var b = new ScalarResult<int>(ScalarStatus.None, 42);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentValue_AreNotEqual()
    {
        var a = new ScalarResult<int>(ScalarStatus.Value, 1);
        var b = new ScalarResult<int>(ScalarStatus.Value, 2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GetHashCode_SameForEqualInstances()
    {
        var a = new ScalarResult<int>(ScalarStatus.Value, 42);
        var b = new ScalarResult<int>(ScalarStatus.Value, 42);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_ContainsStatusAndValue()
    {
        var result = new ScalarResult<int>(ScalarStatus.Value, 42);
        var str = result.ToString();
        Assert.Contains("Value", str);
        Assert.Contains("42", str);
    }

    [Fact]
    public void WithExpression_CreatesModifiedCopy()
    {
        var original = new ScalarResult<int>(ScalarStatus.Value, 42);
        var modified = original with { Status = ScalarStatus.None };

        Assert.Equal(ScalarStatus.None, modified.Status);
        Assert.Equal(42, modified.Value);
        Assert.Equal(ScalarStatus.Value, original.Status); // original unchanged
    }

    [Fact]
    public void NullableInt_WorksWithNull()
    {
        var result = new ScalarResult<int?>(ScalarStatus.Null, null);
        Assert.Equal(ScalarStatus.Null, result.Status);
        Assert.Null(result.Value);
        Assert.False(result.HasValue);
    }

    [Fact]
    public void StringValue_WorksCorrectly()
    {
        var result = new ScalarResult<string>(ScalarStatus.Value, "hello");
        Assert.True(result.HasValue);
        Assert.Equal("hello", result.Required);
    }

    [Fact]
    public void Required_WithNullableIntAndNullValue_ThrowsForNullStatus()
    {
        var result = new ScalarResult<int?>(ScalarStatus.Null, null);
        var ex = Assert.Throws<InvalidOperationException>(() => result.Required);
        Assert.Equal("Query returned a null value.", ex.Message);
    }

    [Fact]
    public void ScalarStatus_AllValuesDistinct()
    {
        // Make sure the enum values are distinguishable
        Assert.NotEqual(ScalarStatus.None, ScalarStatus.Null);
        Assert.NotEqual(ScalarStatus.None, ScalarStatus.Value);
        Assert.NotEqual(ScalarStatus.Null, ScalarStatus.Value);
    }

    [Fact]
    public void DefaultStruct_HasNoneStatus()
    {
        var result = default(ScalarResult<int>);
        Assert.Equal(ScalarStatus.None, result.Status);
        Assert.Equal(0, result.Value);
        Assert.False(result.HasValue);
    }
}