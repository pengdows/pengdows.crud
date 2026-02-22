using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DataReaderMapperFastPathTests
{
    [Fact]
    public async Task LoadAsync_IntegralConversion_UsesTypedGetter()
    {
        await using var reader = new ThrowingGetValueReader<long>("Age", 42L);

        var result = await DataReaderMapper.LoadAsync<IntEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(42, result[0].Age);
    }

    [Fact]
    public async Task LoadAsync_FloatingConversion_UsesTypedGetter()
    {
        await using var reader = new ThrowingGetValueReader<double>("Amount", 42.5d);

        var result = await DataReaderMapper.LoadAsync<DecimalEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.Equal(42.5m, result[0].Amount);
    }

    [Fact]
    public async Task LoadAsync_BooleanConversion_UsesTypedGetter()
    {
        await using var reader = new ThrowingGetValueReader<long>("IsActive", 1L);

        var result = await DataReaderMapper.LoadAsync<BoolEntity>(reader, MapperOptions.Default);

        Assert.Single(result);
        Assert.True(result[0].IsActive);
    }

    private sealed class IntEntity
    {
        public int Age { get; set; }
    }

    private sealed class DecimalEntity
    {
        public decimal Amount { get; set; }
    }

    private sealed class BoolEntity
    {
        public bool IsActive { get; set; }
    }

    private sealed class ThrowingGetValueReader<TField> : fakeDbDataReader
    {
        private readonly TField _value;
        private readonly string _name;

        public ThrowingGetValueReader(string name, TField value)
            : base(new[]
            {
                new System.Collections.Generic.Dictionary<string, object>
                {
                    [name] = value!
                }
            })
        {
            _name = name;
            _value = value;
        }

        public override object GetValue(int ordinal)
        {
            throw new InvalidOperationException("GetValue should not be called for integral fast-path mapping.");
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            if (ordinal != 0)
            {
                throw new IndexOutOfRangeException();
            }

            if (typeof(T) != typeof(TField))
            {
                throw new InvalidOperationException($"Unsupported field type {typeof(T).Name}.");
            }

            return (T)(object)_value!;
        }

        public override Type GetFieldType(int ordinal)
        {
            if (ordinal != 0)
            {
                throw new IndexOutOfRangeException();
            }

            return typeof(TField);
        }

        public override bool IsDBNull(int ordinal) => false;
    }
}
