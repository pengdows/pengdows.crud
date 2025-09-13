using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

public class fakeDbTests
{
    [Fact]
    public void fakeDbParameter_AllowsNullValues()
    {
        var param = new fakeDbParameter
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
    public void fakeDbParameter_DefaultPrecisionAndScale_AreZero()
    {
        var param = new fakeDbParameter();

        Assert.Equal((byte)0, param.Precision);
        Assert.Equal((byte)0, param.Scale);
    }

    [Fact]
    public void fakeDbParameter_PrecisionAndScale_CanBeSet()
    {
        var param = new fakeDbParameter
        {
            Precision = 5,
            Scale = 2
        };

        Assert.Equal((byte)5, param.Precision);
        Assert.Equal((byte)2, param.Scale);
    }

    [Fact]
    public void fakeDbFactory_CreateParameter_ReturnsfakeDbParameter()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var param = factory.CreateParameter();

        Assert.IsType<fakeDbParameter>(param);
    }

    [Fact]
    public void fakeDbFactory_CreateParameter_PrecisionAndScale_CanBeSet()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var param = (fakeDbParameter)factory.CreateParameter();

        Assert.Equal((byte)0, param.Precision);
        Assert.Equal((byte)0, param.Scale);

        param.Precision = 3;
        param.Scale = 1;

        Assert.Equal((byte)3, param.Precision);
        Assert.Equal((byte)1, param.Scale);
    }

    [Fact]
    public void fakeDbDataReader_NullRows_InitializesEmpty()
    {
        var reader = new fakeDbDataReader(null);

        Assert.False(reader.HasRows);
        Assert.Equal(0, reader.FieldCount);
    }

    [Fact]
    public void fakeDbDataReader_GetBytes_CopiesBytesSafely()
    {
        var row = new System.Collections.Generic.Dictionary<string, object>
        {
            ["ByteField"] = new byte[] { 0, 1, 2, 3, 4, 5 }
        };
        using var reader = new fakeDbDataReader(new[] { row });
        Assert.True(reader.Read());

        var buffer = new byte[3];
        var copied = reader.GetBytes(0, 2, buffer, 0, buffer.Length);

        Assert.Equal(3, copied);
        Assert.Equal(new byte[] { 2, 3, 4 }, buffer);
    }

    [Fact]
    public async Task fakeDbCommand_CommandText_AllowsNullAsync()
    {
        var command = new fakeDbCommand();
        command.CommandText = null;

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Null(command.CommandText);
        Assert.Equal(42, result);
    }
}
