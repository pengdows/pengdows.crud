using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests.@internal;

public class ProviderMembersTests
{
    public sealed class Holder
    {
        public int Count { get; } = 7;
        public string Label = "field";
        public long Big { get; } = long.MaxValue;
        public string Text { get; } = "not a number";
    }

    public sealed class Indexed
    {
        public int this[int index] => index;
        public int this[string key] => key.Length;
    }

    public sealed class Pair
    {
        public Pair(int first, int second)
        {
            First = first;
            Second = second;
        }

        public int First { get; }
        public int Second { get; }
    }

    public sealed class Triple
    {
        public Triple(int a, int b, int c) => Sum = a + b + c;
        public int Sum { get; }
    }

    public sealed class Sextet
    {
        public Sextet(int a, int b, int c, int d, int e, int f) => Sum = a + b + c + d + e + f;
        public int Sum { get; }
    }

    [Fact]
    public void Getter_PublicProperty_ReadsTheValue()
    {
        var getter = ProviderMembers.Getter<int>(typeof(Holder), nameof(Holder.Count));

        Assert.NotNull(getter);
        Assert.Equal(7, getter!(new Holder()));
    }

    [Fact]
    public void Getter_PublicField_ReadsTheValue()
    {
        var getter = ProviderMembers.Getter<string>(typeof(Holder), nameof(Holder.Label));

        Assert.NotNull(getter);
        Assert.Equal("field", getter!(new Holder()));
    }

    [Fact]
    public void Getter_MissingMember_ReturnsNull()
    {
        Assert.Null(ProviderMembers.Getter<int>(typeof(Holder), "NoSuchMember"));
    }

    [Fact]
    public void Getter_AmbiguousOverloads_ReturnsNullInsteadOfThrowing()
    {
        // Two indexers share the name "Item"; the lookup is ambiguous and the getter is reported as absent.
        Assert.Null(ProviderMembers.Getter<int>(typeof(Indexed), "Item"));
    }

    [Fact]
    public void Getter_NumericMemberConvertedChecked_ThrowsOnOverflow()
    {
        var getter = ProviderMembers.Getter<int>(typeof(Holder), nameof(Holder.Big));

        Assert.NotNull(getter);
        Assert.Throws<OverflowException>(() => getter!(new Holder()));
    }

    [Fact]
    public void Getter_UnconvertibleMember_ReturnsNull()
    {
        Assert.Null(ProviderMembers.Getter<int>(typeof(Holder), nameof(Holder.Text)));
    }

    [Fact]
    public void Getter_IsCachedPerOwnerNameAndResult()
    {
        var first = ProviderMembers.Getter<int>(typeof(Holder), nameof(Holder.Count));
        var second = ProviderMembers.Getter<int>(typeof(Holder), nameof(Holder.Count));

        Assert.Same(first, second);
    }

    [Fact]
    public void Constructor_TwoArguments_BuildsTheInstance()
    {
        var build = ProviderMembers.Constructor<int, int>(typeof(Pair));

        var pair = Assert.IsType<Pair>(build!(3, 4));
        Assert.Equal(3, pair.First);
        Assert.Equal(4, pair.Second);
    }

    [Fact]
    public void Constructor_ThreeArguments_BuildsTheInstance()
    {
        var build = ProviderMembers.Constructor<int, int, int>(typeof(Triple));

        var triple = Assert.IsType<Triple>(build!(1, 2, 3));
        Assert.Equal(6, triple.Sum);
    }

    [Fact]
    public void Constructor_SixArguments_BuildsTheInstance()
    {
        var build = ProviderMembers.Constructor<int, int, int, int, int, int>(typeof(Sextet));

        var sextet = Assert.IsType<Sextet>(build!(1, 2, 3, 4, 5, 6));
        Assert.Equal(21, sextet.Sum);
    }

    [Fact]
    public void Constructor_NoMatchingConstructor_ReturnsNull()
    {
        Assert.Null(ProviderMembers.Constructor<int, int>(typeof(Triple)));
    }

    [Fact]
    public void Constructor_IsCachedPerType()
    {
        var first = ProviderMembers.Constructor<int, int>(typeof(Pair));
        var second = ProviderMembers.Constructor<int, int>(typeof(Pair));

        Assert.Same(first, second);
    }
}
