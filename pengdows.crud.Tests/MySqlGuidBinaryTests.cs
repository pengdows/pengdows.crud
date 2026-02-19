using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class MySqlGuidBinaryTests
{
    [Fact]
    public void CreateDbParameter_WithDbTypeGuid_ForMySql_UsesGuidDbType()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext("Server=fake;Database=test;", factory, typeMap);

        var value = Guid.NewGuid();
        var parameter = context.CreateDbParameter("id", DbType.Guid, value);

        Assert.Equal(DbType.Guid, parameter.DbType);
        Assert.Equal(value, parameter.Value);
    }

    [Fact]
    public void MapReaderToObject_WithBinaryGuid_MapsToGuid()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext("Server=fake;Database=test;", factory, typeMap);
        typeMap.Register<GuidBinaryEntity>();

        var helper = new TableGateway<GuidBinaryEntity, Guid>(context);
        var expected = Guid.NewGuid();

        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["id"] = expected.ToByteArray()
            }
        };

        using var reader = new FakeTrackedReader(rows);
        reader.Read();

        var entity = helper.MapReaderToObject(reader);

        Assert.Equal(expected, entity.Id);
    }

    [Table("guid_entities")]
    private sealed class GuidBinaryEntity
    {
        [Id(true)]
        [Column("id", DbType.Guid)]
        public Guid Id { get; set; }
    }

    private sealed class FakeTrackedReader : fakeDbDataReader, ITrackedReader
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
    }
}
