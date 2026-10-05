using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Dialects stamp provider-specific parameter metadata (NpgsqlDbType, DataTypeName, OracleDbType,
/// OracleCommand.InitialLONGFetchSize) by reflection. Each call looked the property up, parsed the
/// enum name and set it through PropertyInfo.SetValue: a PostgreSQL Guid parameter measured 129 ns
/// and 136 B against 40 ns and 96 B for an int. Stamping must allocate nothing.
/// </summary>
[Collection("AllocationSerial")]
public sealed class ProviderParameterMetadataCostTests
{
    public enum Mood
    {
        Happy,
        Sad
    }

    [Table("t")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("mood", DbType.String)] public Mood Mood { get; set; }
        [Column("doc", DbType.Object)] public JsonValue Doc { get; set; }
    }

    private static ISqlDialect Dialect(SupportedDatabase database, Action<fakeDbFactory>? configure = null)
    {
        var factory = new fakeDbFactory(database);
        configure?.Invoke(factory);
        return new DatabaseContext($"Data Source=x;EmulatedProduct={database}", factory).Dialect;
    }

    private static long Allocated(Action action) => AllocationMeasurement.Lowest(() => AllocatedOnce(action));

    private static long AllocatedOnce(Action action)
    {
        action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            action();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
    }

    private static IColumnInfo Column(string name) =>
        new TypeMapRegistry().GetTableInfo<Row>().Columns.Values.First(c => c.Name == name);

    [Fact]
    public void PostgreSqlGuidParameter_StampsUuid_WithoutAllocating()
    {
        var dialect = Dialect(SupportedDatabase.PostgreSql, f => f.EmulatesNpgsqlParameterMetadata = true);
        var guid = Guid.NewGuid();

        var parameter = (fakeDbNpgsqlParameter)dialect.CreateDbParameter("p", DbType.Guid, guid);

        Assert.Equal(fakeNpgsqlDbType.Uuid, parameter.NpgsqlDbType);
        // The PostgreSQL Guid mapping's stamp, on its own (a fresh parameter would measure fakeDb's).
        Assert.Equal(0, Allocated(() => types.AdvancedTypeRegistry.SetEnumProperty(parameter, "NpgsqlDbType", "Uuid")));
    }

    [Fact]
    public void PostgreSqlEnumColumn_MarksUnknown_WithoutAllocating()
    {
        var dialect = Dialect(SupportedDatabase.PostgreSql, f => f.EmulatesNpgsqlParameterMetadata = true);
        var column = Column("mood");
        var parameter = new fakeDbNpgsqlParameter();

        dialect.MarkColumnParameter(parameter, column);

        Assert.Equal(fakeNpgsqlDbType.Unknown, parameter.NpgsqlDbType);
        Assert.Equal(0, Allocated(() => dialect.MarkColumnParameter(parameter, column)));
    }

    [Fact]
    public void PostgreSqlJsonColumn_MarksJsonb_WithoutAllocating()
    {
        var dialect = Dialect(SupportedDatabase.PostgreSql, f => f.EmulatesNpgsqlParameterMetadata = true);
        var column = Column("doc");
        var parameter = new fakeDbNpgsqlParameter();

        dialect.MarkColumnParameter(parameter, column);

        Assert.Equal(fakeNpgsqlDbType.Jsonb, parameter.NpgsqlDbType);
        Assert.Equal("jsonb", parameter.DataTypeName);
        Assert.Equal(0, Allocated(() => dialect.MarkColumnParameter(parameter, column)));
    }

    [Theory]
    [InlineData(DbType.Double, fakeOracleDbType.BinaryDouble)]
    [InlineData(DbType.Single, fakeOracleDbType.BinaryFloat)]
    public void OracleFloatingPointParameter_StampsBinaryType_AllocatingNothingExtra(DbType type, fakeOracleDbType expected)
    {
        var dialect = Dialect(SupportedDatabase.Oracle, f => f.EmulatesOracleParameterMetadata = true);
        object value = type == DbType.Double ? 1.5 : 1.5f;

        var parameter = (fakeDbOracleParameter)dialect.CreateDbParameter("p", type, value);

        Assert.Equal(expected, parameter.OracleDbType);
        var floatingBytes = Allocated(() => dialect.CreateDbParameter("p", type, value));
        var intBytes = Allocated(() => dialect.CreateDbParameter("p", DbType.Int32, (object)42));
        Assert.True(floatingBytes <= intBytes, $"{type} {floatingBytes} B, int {intBytes} B");
    }

    [Fact]
    public void OracleConfigureCommand_FetchesLongWithTheRow_WithoutAllocating()
    {
        var dialect = (SqlDialect)Dialect(SupportedDatabase.Oracle);
        var command = new fakeDbCommand();

        dialect.ConfigureCommand(command);

        Assert.Equal(-1, command.InitialLONGFetchSize);
        Assert.Equal(0, Allocated(() => dialect.ConfigureCommand(command)));
    }
}
