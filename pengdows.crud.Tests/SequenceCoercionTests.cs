using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-011: sequence coercion (vector and LIST columns into T[] / List&lt;T&gt;) built a List through
/// Activator, added boxed elements through IList and copied it into an array, and coerced even
/// elements already of the target type: a 1536-dimension embedding cost about 240 us and 133 KB.
/// The result must stay what coercing each element would give.
/// </summary>
[Collection("AllocationSerial")]
public sealed class SequenceCoercionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { new[] { 1.5f, -2.25f, float.MaxValue }, typeof(float[]) };
        yield return new object[] { new List<double> { 1.5, -2.25, 1e30, double.NaN }, typeof(float[]) };
        yield return new object[] { new List<double> { 1.5, -2.25 }, typeof(List<double>) };
        yield return new object[] { new[] { 1, 2, int.MaxValue }, typeof(long[]) };
        yield return new object[] { new List<object?> { 1, null, 3L }, typeof(int?[]) };
        yield return new object[] { new List<object?> { 1, null, 3L }, typeof(List<long?>) };
        yield return new object[] { "[1.5, -2.25, 3e2]", typeof(float[]) };
        yield return new object[] { new[] { "a", "b" }, typeof(List<string>) };
    }

    private static Type ElementType(Type target) =>
        target.IsArray ? target.GetElementType()! : target.GetGenericArguments()[0];

    private static IEnumerable<object?> ExpectedElements(object source, Type elementType)
    {
        IEnumerable items = source is string text
            ? System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateArray().Select(e => e.GetRawText()).ToArray()
            : (IEnumerable)source;
        foreach (var item in items)
        {
            yield return item is null ? null : TypeCoercionHelper.Coerce(item, item.GetType(), elementType);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sequence_CoercesToWhatEachElementCoercesTo(object source, Type target)
    {
        var result = TypeCoercionHelper.Coerce(source, source.GetType(), target);

        Assert.NotNull(result);
        Assert.IsType(target, result);
        Assert.Equal(ExpectedElements(source, ElementType(target)).ToList(), ((IEnumerable)result!).Cast<object?>().ToList());
    }

    [Fact]
    public void SameElementType_AllocatesOnlyTheResultArray()
    {
        var vector = Enumerable.Range(0, 1536).Select(i => i * 0.5f).ToArray();
        TypeCoercionHelper.Coerce(vector, typeof(float[]), typeof(float[]));
        var asList = new List<float>(vector);
        TypeCoercionHelper.Coerce(asList, typeof(List<float>), typeof(float[]));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = (float[])TypeCoercionHelper.Coerce(asList, typeof(List<float>), typeof(float[]))!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(vector, result);
        // The float[1536] itself is about 6 KB.
        Assert.True(allocated < 8 * 1024, $"{allocated} B");
    }
}
