#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperInvalidValueExceptionTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<SetterThrowsEntity, int> _helper;

    public EntityHelperInvalidValueExceptionTests()
    {
        TypeMap.Register<SetterThrowsEntity>();
        _helper = new EntityHelper<SetterThrowsEntity, int>(Context);
    }

    [Fact]
    public void MapReaderToObject_PropertySetterThrows_ThrowsInvalidValueException()
    {
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Name"] = "bad"
            }
        };

        using var reader = new FakeTrackedReader(rows);
        reader.Read();

        Assert.Throws<InvalidValueException>(() => _helper.MapReaderToObject(reader));
    }

    [Fact]
    public void MapReaderToObject_ValidValue_ReturnsEntity()
    {
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Name"] = "good"
            }
        };

        using var reader = new FakeTrackedReader(rows);
        reader.Read();

        var entity = _helper.MapReaderToObject(reader);
        Assert.Equal("good", entity.Name);
    }

    [Table("SetterThrows")]
    private sealed class SetterThrowsEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        private string _name = string.Empty;

        [Column("Name", DbType.String)]
        public string Name
        {
            get => _name;
            set
            {
                if (value == "bad")
                {
                    throw new Exception("bad value");
                }

                _name = value;
            }
        }
    }

    private sealed class FakeTrackedReader : FakeDbDataReader, ITrackedReader
    {
        public FakeTrackedReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public new Task<bool> ReadAsync()
        {
            return base.ReadAsync(CancellationToken.None);
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public override Type GetFieldType(int ordinal)
        {
            var value = GetValue(ordinal);
            return value?.GetType() ?? typeof(object);
        }
    }
}

