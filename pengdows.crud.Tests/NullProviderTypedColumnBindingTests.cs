using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// WRT-011, found by the live type matrix: a NULL written to a provider-typed column was bound as
/// DbType.Object with no value, which no driver can type: SQL Server sent sql_variant ("Operand type
/// clash: sql_variant is incompatible with vector"), Oracle refused it (ORA-50028), Snowflake.Data
/// threw "No corresponding Snowflake type for type Object". A null now takes the DbType the dialect
/// binds a value of that column with: vector text, Snowflake's spatial text.
/// </summary>
public sealed class NullProviderTypedColumnBindingTests
{
    [Table("t")]
    public sealed class WithVector
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Object)] public float[]? V { get; set; }
    }

    [Table("t")]
    public sealed class WithSpatial
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("g", DbType.Object)] public Geography? G { get; set; }
        [Column("m", DbType.Object)] public Geometry? M { get; set; }
    }

    private static List<DbParameter> Parameters(ISqlContainer sc) =>
        ((IDictionary<string, DbParameter>)typeof(SqlContainer)
            .GetField("_parameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(sc)!).Values.ToList();

    private static DatabaseContext Context(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    public void NullVector_BindsAsText(SupportedDatabase db)
    {
        using var context = Context(db);
        using var sc = new TableGateway<WithVector, int>(context).BuildCreate(new WithVector { Id = 1 });

        var vector = Parameters(sc).Single(p => p.DbType != DbType.Int32 && !(p.Value is int));
        Assert.Equal(DbType.String, vector.DbType);
    }

    [Fact]
    public void NullSpatial_OnSnowflake_BindsAsText()
    {
        using var context = Context(SupportedDatabase.Snowflake);
        using var sc = new TableGateway<WithSpatial, int>(context).BuildCreate(new WithSpatial { Id = 1 });

        var spatial = Parameters(sc).Where(p => !(p.Value is int)).ToList();
        Assert.Equal(2, spatial.Count);
        Assert.All(spatial, p => Assert.Equal(DbType.String, p.DbType));
    }

    [Fact]
    public void NullVector_OnADatabaseThatBindsVectorsNatively_IsUnchanged()
    {
        using var context = Context(SupportedDatabase.PostgreSql);
        using var sc = new TableGateway<WithVector, int>(context).BuildCreate(new WithVector { Id = 1 });

        var vector = Parameters(sc).Single(p => !(p.Value is int));
        Assert.Equal(DbType.Object, vector.DbType);
    }
}
