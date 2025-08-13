using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

public class FakeDbTests
{
    [Fact]
    public void FakeDbParameter_AllowsNullValues()
    {
        var param = new FakeDbParameter
        {
            ParameterName = null,
            SourceColumn = null,
            Value = null,
            DbType = DbType.Int32
        };

        param.ResetDbType();

        Assert.Null(param.ParameterName);
        Assert.Null(param.SourceColumn);
        Assert.Null(param.Value);
        Assert.Equal(DbType.Object, param.DbType);
    }

    [Fact]
    public void FakeDbDataReader_NullRows_InitializesEmpty()
    {
        var reader = new FakeDbDataReader(null);

        Assert.False(reader.HasRows);
        Assert.Equal(0, reader.FieldCount);
    }

    [Fact]
    public void FakeDbDataReader_GetBytes_Throws()
    {
        var reader = new FakeDbDataReader();
        Assert.Throws<NotSupportedException>(() => reader.GetBytes(0, 0, null, 0, 0));
    }

    [Fact]
    public async Task FakeDbCommand_CommandText_AllowsNullAsync()
    {
        var command = new FakeDbCommand();
        command.CommandText = null;

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Null(command.CommandText);
        Assert.Equal(42, result);
    }
}
