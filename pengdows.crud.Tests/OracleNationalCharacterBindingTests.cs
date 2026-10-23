using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// ODP.NET binds DbType.String as VARCHAR2, which Oracle converts through the database character set.
/// On a database with a non-Unicode one (live: Oracle 12.2 enterprise image, WE8DEC) text bound that way
/// lost every character outside it before reaching an NVARCHAR2 column ("héllo wörld ✓ 😀" read back
/// with ¿ for ✓ and the emoji). A column the TYPE-020 probe reports as national (NVARCHAR2, NCHAR,
/// NCLOB) binds with the matching national type; other string columns keep VARCHAR2, since a national
/// bind compared to a VARCHAR2 column makes Oracle convert the column and can lose its index.
/// </summary>
public sealed class OracleNationalCharacterBindingTests
{
    public enum OracleDbTypeStub { Varchar2 = 126, NVarchar2 = 119, NChar = 117, NClob = 116, Clob = 105 }

    private sealed class OracleLikeParameter : fakeDbParameter
    {
        public OracleDbTypeStub OracleDbType { get; set; } = OracleDbTypeStub.Varchar2;
    }

    private sealed class OracleLikeFactory : DbProviderFactory
    {
        public override DbParameter CreateParameter() => new OracleLikeParameter();
        public override DbConnection CreateConnection() => new fakeDbConnection();
        public override DbCommand CreateCommand() => new fakeDbCommand();
    }

    [Table("nchar_t")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("txt", DbType.String)] public string Text { get; set; } = "";
    }

    private static (OracleDialect Dialect, IColumnInfo Column) Learned(string? declared)
    {
        var dialect = new OracleDialect(new OracleLikeFactory(), NullLogger<OracleDialect>.Instance);
        var column = new TypeMapRegistry().GetTableInfo<Row>().Columns["txt"];
        if (declared != null)
        {
            dialect.RecordDeclaredTypes("\"nchar_t\"", new[] { (column, declared) });
        }

        return (dialect, column);
    }

    [Fact]
    public void StringColumn_DeclaredTypeIsLearnedBeforeAWrite()
    {
        var (dialect, column) = Learned(null);

        Assert.True(dialect.NeedsDeclaredType(column, forRead: false));
    }

    [Theory]
    [InlineData("NVarchar2", OracleDbTypeStub.NVarchar2)]
    [InlineData("NChar", OracleDbTypeStub.NChar)]
    [InlineData("NClob", OracleDbTypeStub.NClob)]
    public void NationalColumn_BindsWithTheNationalType(string declared, OracleDbTypeStub expected)
    {
        var (dialect, column) = Learned(declared);
        var parameter = (OracleLikeParameter)dialect.CreateDbParameter("p", DbType.String, "héllo wörld ✓ 😀");

        Assert.True(dialect.MarksColumnParameter(column));
        dialect.MarkColumnParameter(parameter, column);

        Assert.Equal(expected, parameter.OracleDbType);
        Assert.Equal("héllo wörld ✓ 😀", parameter.Value);
    }

    [Theory]
    [InlineData("Varchar2")]
    [InlineData(null)]
    public void NonNationalOrUnknownColumn_KeepsVarchar2(string? declared)
    {
        var (dialect, column) = Learned(declared);
        var parameter = (OracleLikeParameter)dialect.CreateDbParameter("p", DbType.String, "x");

        Assert.False(dialect.MarksColumnParameter(column));
        dialect.MarkColumnParameter(parameter, column);

        Assert.Equal(OracleDbTypeStub.Varchar2, parameter.OracleDbType);
    }
}
