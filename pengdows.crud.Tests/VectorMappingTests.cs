using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-015, probed live 2026-09-30: SQL Server 2025 VECTOR reads through SqlClient 6.0 as text
/// ("[1.5000000e+000,...]") and through SqlClient 6.1+ as SqlVector&lt;float&gt; (Memory property);
/// both reject a float[] parameter but convert text. Oracle 23ai/26ai VECTOR reads as float[] and
/// converts text, while a float[] parameter needs OracleDbType.Vector. pgvector: Npgsql binds a
/// float[] as real[], which pgvector's assignment cast accepts, and reads the column only through
/// the Pgvector.Npgsql plugin (Pgvector.Vector, ToArray()).
/// </summary>
public sealed class VectorMappingTests
{
    [Fact]
    public void Coerce_SqlServerVectorText_ToFloatAndDoubleArraysAndLists()
    {
        const string text = "[1.5000000e+000,2.0000000e+000,-3.0000000e+000]";

        Assert.Equal(new[] { 1.5f, 2f, -3f }, TypeCoercionHelper.Coerce(text, typeof(string), typeof(float[])));
        Assert.Equal(new[] { 1.5d, 2d, -3d }, TypeCoercionHelper.Coerce(text, typeof(string), typeof(double[])));
        Assert.Equal(new List<float> { 1.5f, 2f, -3f }, TypeCoercionHelper.Coerce(text, typeof(string), typeof(List<float>)));
        Assert.Equal(Array.Empty<float>(), TypeCoercionHelper.Coerce("[]", typeof(string), typeof(float[])));
    }

    [Fact]
    public void Coerce_TextThatIsNotAVector_Throws()
    {
        Assert.ThrowsAny<Exception>(() => TypeCoercionHelper.Coerce("not a vector", typeof(string), typeof(float[])));
        Assert.ThrowsAny<Exception>(() => TypeCoercionHelper.Coerce("[1,\"x\"]", typeof(string), typeof(float[])));
    }

    [Fact]
    public void Coerce_ProviderVectorTypes_ToFloatArray()
    {
        var expected = new[] { 1.5f, 2f, -3f };

        Assert.Equal(expected, TypeCoercionHelper.Coerce(new SqlVector<float>(expected), typeof(SqlVector<float>), typeof(float[])));
        Assert.Equal(expected, TypeCoercionHelper.Coerce(new Pgvector.Vector(expected), typeof(Pgvector.Vector), typeof(float[])));
        Assert.Equal(expected, TypeCoercionHelper.Coerce(new ReadOnlyMemory<float>(expected), typeof(ReadOnlyMemory<float>), typeof(float[])));
        Assert.Equal(new[] { 1.5d, 2d, -3d }, TypeCoercionHelper.Coerce(expected, typeof(float[]), typeof(double[])));
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    public void CreateDbParameter_FloatArrayOnAVectorTextDatabase_BindsExactText(SupportedDatabase database)
    {
        using var context = new DatabaseContext($"Data Source=x;EmulatedProduct={database}", new fakeDbFactory(database));
        var values = new[] { 0.1f, -0f, float.MaxValue, 1.0000001f };

        var parameter = context.CreateDbParameter("p", DbType.Object, values);

        Assert.Equal(DbType.String, parameter.DbType);
        var text = Assert.IsType<string>(parameter.Value);
        Assert.Equal("[0.1,-0,3.4028235E+38,1.0000001]", text);
        // The text is exact: every element parses back to the same bits.
        var parsed = Array.ConvertAll(text.Trim('[', ']').Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
        Assert.Equal(Array.ConvertAll(values, BitConverter.SingleToInt32Bits), Array.ConvertAll(parsed, BitConverter.SingleToInt32Bits));
    }

    [Fact]
    public void CreateDbParameter_DoubleArrayOnSqlServer_BindsText()
    {
        using var context = new DatabaseContext("Data Source=x;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer));

        var parameter = context.CreateDbParameter("p", DbType.Object, new[] { 1.5d, -2d });

        Assert.Equal("[1.5,-2]", parameter.Value);
    }

    [Fact]
    public void CreateDbParameter_FloatArrayOnPostgreSql_StaysAnArray()
    {
        using var context = new DatabaseContext("Data Source=x;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql));
        var values = new[] { 1.5f, 2f };

        var parameter = context.CreateDbParameter("p", DbType.Object, values);

        Assert.Same(values, parameter.Value);
    }

    // Stands in for Microsoft.Data.SqlTypes.SqlVector<T> (SqlClient 6.1+), recognized by name.
    private sealed class SqlVector<T> where T : unmanaged
    {
        public SqlVector(T[] values) => Memory = values;
        public ReadOnlyMemory<T> Memory { get; }
    }
}
