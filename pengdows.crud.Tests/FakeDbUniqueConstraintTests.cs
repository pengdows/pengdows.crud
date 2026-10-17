using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.configuration;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// F2: a keyed-constraint model in fakeDb. A table with a declared unique key refuses a duplicate with the violation the
/// emulated product's provider raises, so the library's own classification, translation and retry paths run end to end
/// instead of being fed a canned exception. It is a model: <c>MERGE</c> is not modelled (see docs/testing-with-fakedb.md).
/// </summary>
public sealed class FakeDbUniqueConstraintTests
{
    [Table("fk_rows")]
    public sealed class Row
    {
        [Id(true)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("code", DbType.String)] public string Code { get; set; } = "";
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    private static (fakeDbFactory Factory, DatabaseContext Context) Create(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db) { EnableDataPersistence = true };
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        }, factory);
        return (factory, context);
    }

    public static TheoryData<SupportedDatabase> Products()
    {
        var data = new TheoryData<SupportedDatabase>();
        foreach (var db in Enum.GetValues<SupportedDatabase>().Where(d => d != SupportedDatabase.Unknown))
        {
            data.Add(db);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Products))]
    public async Task DuplicateKey_RaisesTheViolationThatProductsDialectRecognises(SupportedDatabase db)
    {
        var (factory, context) = Create(db);
        await using var _ = context;
        factory.DataStore.DefineUniqueKey("fk_rows", "id");
        var gateway = new TableGateway<Row, long>(context);
        await gateway.CreateAsync(new Row { Id = 1, Code = "a", Name = "first" });

        var ex = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            gateway.CreateAsync(new Row { Id = 1, Code = "b", Name = "second" }).AsTask());

        Assert.IsAssignableFrom<DbException>(ex.InnerException);
        Assert.True(context.Dialect.IsUniqueViolation((DbException)ex.InnerException!));
    }

    [Fact]
    public async Task NonKeyColumnsMayRepeat_AndDifferentKeysAreAccepted()
    {
        var (factory, context) = Create(SupportedDatabase.PostgreSql);
        await using var _ = context;
        factory.DataStore.DefineUniqueKey("fk_rows", "code");
        var gateway = new TableGateway<Row, long>(context);

        await gateway.CreateAsync(new Row { Id = 1, Code = "a", Name = "same" });
        await gateway.CreateAsync(new Row { Id = 2, Code = "b", Name = "same" });

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            gateway.CreateAsync(new Row { Id = 3, Code = "a", Name = "other" }).AsTask());
    }

    [Fact]
    public async Task NoDeclaredKey_BehavesAsBefore()
    {
        var (_, context) = Create(SupportedDatabase.PostgreSql);
        await using var _ = context;
        var gateway = new TableGateway<Row, long>(context);

        await gateway.CreateAsync(new Row { Id = 1, Code = "a", Name = "x" });
        await gateway.CreateAsync(new Row { Id = 1, Code = "a", Name = "x" });
    }

    [Fact]
    public async Task OnConflictUpsert_IsAtomic_UpdatesTheExistingRowInsteadOfRaising()
    {
        var (factory, context) = Create(SupportedDatabase.Sqlite);
        await using var _ = context;
        factory.DataStore.DefineUniqueKey("fk_rows", "id");
        var gateway = new TableGateway<Row, long>(context);
        await gateway.CreateAsync(new Row { Id = 1, Code = "a", Name = "first" });

        await gateway.UpsertAsync(new Row { Id = 1, Code = "a", Name = "second" });

        var row = await gateway.RetrieveOneAsync(1);
        Assert.Equal("second", row!.Name);
    }

    [Fact]
    public async Task ConcurrentInserts_OfOneKey_LetExactlyOneWin()
    {
        var (factory, context) = Create(SupportedDatabase.PostgreSql);
        await using var _ = context;
        factory.DataStore.DefineUniqueKey("fk_rows", "id");
        var gateway = new TableGateway<Row, long>(context);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
        {
            try
            {
                await gateway.CreateAsync(new Row { Id = 7, Code = "c" + i, Name = "n" });
                return true;
            }
            catch (UniqueConstraintViolationException)
            {
                return false;
            }
        }));

        Assert.Equal(1, results.Count(won => won));
    }
}
