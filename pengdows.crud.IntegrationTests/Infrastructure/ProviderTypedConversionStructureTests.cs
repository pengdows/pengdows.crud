using System.Data.Common;
using System.Linq.Expressions;
using System.Net;
using System.Net.NetworkInformation;
using NpgsqlTypes;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// A provider's own struct (NpgsqlRange, NpgsqlCidr, PhysicalAddress, the hstore dictionary) read into the library's
/// portable value is one typed expression: the provider's value is read as itself and the portable value is built from
/// its members. It used to go through a boxed read, the coercion registry and half a dozen delegates, about 120 ns a
/// value. The read is judged by what it contains: no delegate call and no generic Coerce.
/// </summary>
public class ProviderTypedConversionStructureTests
{
    public sealed class Plain<T>
    {
        public T V { get; set; } = default!;
    }

    private sealed class Inspect : ExpressionVisitor
    {
        public int DelegateInvocations;
        public int GenericCoerceCalls;
        public int OptionsConstants;

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            DelegateInvocations++;
            return base.VisitInvocation(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Type == typeof(TypeCoercionOptions))
            {
                OptionsConstants++;
            }

            return base.VisitConstant(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(TypeCoercionHelper))
            {
                GenericCoerceCalls++;
            }

            return base.VisitMethodCall(node);
        }
    }

    private static (int Invocations, int Coerce) PlanReadOf<T>(Type fieldType)
    {
        var obj = Expression.Parameter(typeof(Plain<T>), "target");
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var key = new DataReaderMapper.SetterCacheKey(typeof(Plain<T>), typeof(Plain<T>).GetProperty("V")!, fieldType,
            RequiresCoercion: true, EnumParseFailureMode.Throw, Ordinal: 0, Coercion: null);
        var body = DataReaderMapper.BuildSetterBody<Plain<T>>(key, obj, reader, out _);
        var inspect = new Inspect();
        inspect.Visit(body);
        return (inspect.DelegateInvocations, inspect.GenericCoerceCalls);
    }

    private static void AssertTyped((int Invocations, int Coerce) read, string shape)
    {
        Assert.True(read.Invocations == 0, $"{shape}: {read.Invocations} delegate call(s) in the read");
        Assert.True(read.Coerce == 0, $"{shape}: {read.Coerce} generic Coerce call(s) in the read");
    }

    [Fact] public void NpgsqlRangeOfInt_IntoRange() => AssertTyped(PlanReadOf<Range<int>>(typeof(NpgsqlRange<int>)), "int4range");
    [Fact] public void NpgsqlRangeOfLong_IntoRange() => AssertTyped(PlanReadOf<Range<long>>(typeof(NpgsqlRange<long>)), "int8range");
    [Fact] public void NpgsqlRangeOfDecimal_IntoRange() => AssertTyped(PlanReadOf<Range<decimal>>(typeof(NpgsqlRange<decimal>)), "numrange");
    [Fact] public void NpgsqlRangeOfDateTime_IntoRange() => AssertTyped(PlanReadOf<Range<DateTime>>(typeof(NpgsqlRange<DateTime>)), "tsrange");
    [Fact] public void NpgsqlRangeOfDateTime_IntoRangeOfOffset() => AssertTyped(PlanReadOf<Range<DateTimeOffset>>(typeof(NpgsqlRange<DateTime>)), "tstzrange");
    [Fact]
    public void NpgsqlRangeOfDateTime_IntoRangeOfOffset_CarriesNoOptionsConstant()
    {
        var obj = Expression.Parameter(typeof(Plain<Range<DateTimeOffset>>), "target");
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var key = new DataReaderMapper.SetterCacheKey(typeof(Plain<Range<DateTimeOffset>>),
            typeof(Plain<Range<DateTimeOffset>>).GetProperty("V")!, typeof(NpgsqlRange<DateTime>),
            RequiresCoercion: true, EnumParseFailureMode.Throw, Ordinal: 0, Coercion: null);
        var body = DataReaderMapper.BuildSetterBody<Plain<Range<DateTimeOffset>>>(key, obj, reader, out _);
        var inspect = new Inspect();
        inspect.Visit(body);

        Assert.True(inspect.OptionsConstants == 0,
            $"tstzrange: {inspect.OptionsConstants} TypeCoercionOptions closure constant(s) in the read");
    }

    [Fact] public void NpgsqlCidr_IntoCidr() => AssertTyped(PlanReadOf<Cidr>(typeof(NpgsqlCidr)), "cidr");
    [Fact] public void PhysicalAddress_IntoMacAddress() => AssertTyped(PlanReadOf<MacAddress>(typeof(PhysicalAddress)), "macaddr");
    [Fact] public void HStoreDictionary_IntoHStore() => AssertTyped(PlanReadOf<HStore>(typeof(Dictionary<string, string?>)), "hstore");

    [Fact]
    public void TheSharedBuilder_KnowsEveryShape()
    {
        var record = Expression.Parameter(typeof(System.Data.IDataRecord), "record");
        (Type Field, Type Target)[] shapes =
        [
            (typeof(NpgsqlRange<int>), typeof(Range<int>)),
            (typeof(NpgsqlRange<DateTime>), typeof(Range<DateTimeOffset>)),
            (typeof(NpgsqlCidr), typeof(Cidr)),
            (typeof(PhysicalAddress), typeof(MacAddress)),
            (typeof(Dictionary<string, string?>), typeof(HStore))
        ];

        foreach (var (field, target) in shapes)
        {
            Assert.NotNull(ProviderTypedConversions.TryBuild(field, target, record, 0));
        }
    }
}
