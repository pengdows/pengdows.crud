using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Declared column types (TYPE-020): where a dialect can only write a column correctly when it knows
/// the column's declared database type, the gateway learns it once per table and context, from the
/// provider's own metadata for a zero-row "SELECT cols FROM t WHERE 1 = 0" run by the first async
/// operation. Build* methods never query; they use the types once known. A failed probe changes
/// nothing. First consumer: PostgreSQL, where a string property bound to a user-defined ENUM (or any
/// non-text) column is sent untyped so the server applies the column's type.
/// </summary>
public class DeclaredColumnTypesTests
{
    [Table("notes")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("mood", DbType.String)] public string Mood { get; set; } = "happy";
        [Column("body", DbType.String)] public string Body { get; set; } = "text body";
    }

    private const string Probe = "SELECT \"mood\", \"body\" FROM \"notes\" WHERE 1 = 0";

    private static (DatabaseContext Context, fakeDbFactory Factory) Postgres(bool declare = true)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql) { EmulatesNpgsqlParameterMetadata = true };
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard
        }, factory);
        var probe = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        if (declare)
        {
            probe.EnqueueReaderResult(new fakeDbDataReader(Array.Empty<Dictionary<string, object>>())
            {
                Columns = new[]
                {
                    new fakeDbColumn("id", typeof(int), "integer"),
                    new fakeDbColumn("mood", typeof(string), "pengdows_mood"),
                    new fakeDbColumn("body", typeof(string), "text")
                }
            });
        }

        factory.Connections.Add(probe);
        return (context, factory);
    }

    private static IEnumerable<fakeDbConnection> ProbeConnections(fakeDbFactory factory) =>
        factory.CreatedConnections.Where(c => c.ExecutedReaderTexts.Any(t => t.Contains("WHERE 1 = 0")));

    private static fakeDbNpgsqlParameter ParamFor(ISqlContainer sc, string value) =>
        ((IDictionary<string, DbParameter>)typeof(SqlContainer)
            .GetField("_parameters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sc)!)
        .Values.Cast<fakeDbNpgsqlParameter>().Single(p => Equals(p.Value, value));

    [Fact]
    public async Task FirstAsyncWrite_ProbesTheTable_ThenAStringIntoAUserTypeIsUntyped()
    {
        var (context, factory) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<Row, int>(context);

        await gateway.CreateAsync(new Row { Id = 1 });

        Assert.Contains(ProbeConnections(factory), c => c.ExecutedReaderTexts.Contains(Probe));
        using var sc = gateway.BuildCreate(new Row { Id = 2 });
        Assert.Equal(fakeNpgsqlDbType.Unknown, ParamFor(sc, "happy").NpgsqlDbType);
        Assert.NotEqual(fakeNpgsqlDbType.Unknown, ParamFor(sc, "text body").NpgsqlDbType);
    }

    [Table("keyed_notes")]
    public sealed class KeyedRow
    {
        [PrimaryKey(1)] [Column("code", DbType.Int32)] public int Code { get; set; }
        [Column("mood", DbType.String)] public string Mood { get; set; } = "happy";
    }

    public static IEnumerable<object[]> EntryPoints() => new (string, Func<IDatabaseContext, Task>)[]
    {
        ("UpdateAsync", c => new TableGateway<Row, int>(c).UpdateAsync(new Row { Id = 1 }).AsTask()),
        ("BuildUpdateAsync", c => new TableGateway<Row, int>(c).BuildUpdateAsync(new Row { Id = 1 }).AsTask()),
        ("UpsertAsync", c => new TableGateway<Row, int>(c).UpsertAsync(new Row { Id = 1 }).AsTask()),
        ("BatchCreateAsync", c => new TableGateway<Row, int>(c).BatchCreateAsync(new[] { new Row { Id = 1 } }).AsTask()),
        ("BatchUpdateAsync", c => new TableGateway<Row, int>(c).BatchUpdateAsync(new[] { new Row { Id = 1 } }).AsTask()),
        ("BatchUpsertAsync", c => new TableGateway<Row, int>(c).BatchUpsertAsync(new[] { new Row { Id = 1 } }).AsTask()),
        ("PK CreateAsync", c => new PrimaryKeyTableGateway<KeyedRow>(c).CreateAsync(new KeyedRow { Code = 1 }).AsTask()),
        ("PK UpdateAsync", c => new PrimaryKeyTableGateway<KeyedRow>(c).UpdateAsync(new KeyedRow { Code = 1 }).AsTask()),
        ("PK UpsertAsync", c => new PrimaryKeyTableGateway<KeyedRow>(c).UpsertAsync(new KeyedRow { Code = 1 }).AsTask()),
        ("PK BatchCreateAsync", c => new PrimaryKeyTableGateway<KeyedRow>(c).BatchCreateAsync(new[] { new KeyedRow { Code = 1 } }).AsTask()),
        ("PK BatchUpdateAsync", c => new PrimaryKeyTableGateway<KeyedRow>(c).BatchUpdateAsync(new[] { new KeyedRow { Code = 1 } }).AsTask()),
        ("PK BatchUpsertAsync", c => new PrimaryKeyTableGateway<KeyedRow>(c).BatchUpsertAsync(new[] { new KeyedRow { Code = 1 } }).AsTask()),
    }.Select(e => new object[] { e.Item1, e.Item2 });

    public static IEnumerable<object[]> ReadEntryPoints() => new (string, Func<IDatabaseContext, Task>)[]
    {
        ("RetrieveOneAsync(id)", c => new TableGateway<Row, int>(c).RetrieveOneAsync(1).AsTask()),
        ("RetrieveAsync", c => new TableGateway<Row, int>(c).RetrieveAsync(new[] { 1 }).AsTask()),
        ("RetrieveStreamAsync", async c => { await foreach (var _ in new TableGateway<Row, int>(c).RetrieveStreamAsync(new[] { 1 })) { } }),
        ("PK RetrieveOneAsync", c => new PrimaryKeyTableGateway<KeyedRow>(c).RetrieveOneAsync(new KeyedRow { Code = 1 }).AsTask()),
    }.Select(e => new object[] { e.Item1, e.Item2 });

    // PostgreSQL needs declared types only to write; a read there sends no probe.
    [Theory]
    [MemberData(nameof(ReadEntryPoints))]
    public async Task Reads_WhenTheDialectNeedsTypesOnlyToWrite_DontProbe(string name, Func<IDatabaseContext, Task> call)
    {
        var (context, factory) = Postgres(declare: false);
        await using var _ = context;

        await call(context);

        Assert.False(factory.CreatedConnections.SelectMany(c => c.ExecutedReaderTexts).Any(t => t.EndsWith("WHERE 1 = 0", StringComparison.Ordinal)),
            $"{name} probed");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public async Task EveryAsyncWrite_LearnsTheDeclaredTypesFirst(string name, Func<IDatabaseContext, Task> call)
    {
        var (context, factory) = Postgres(declare: false);
        await using var _ = context;

        try
        {
            await call(context);
        }
        catch (pengdows.crud.exceptions.DatabaseException)
        {
            // fakeDb answers with no rows; a version check may throw. Only the probe matters here.
        }
        catch (InvalidOperationException)
        {
        }

        Assert.True(factory.CreatedConnections.SelectMany(c => c.ExecutedReaderTexts).Any(t => t.EndsWith("WHERE 1 = 0", StringComparison.Ordinal)),
            $"{name} did not learn the declared column types");
    }

    [Fact]
    public void BuildMethods_NeverQuery()
    {
        var (context, factory) = Postgres();
        using var _ = context;
        var gateway = new TableGateway<Row, int>(context);

        using var sc = gateway.BuildCreate(new Row { Id = 1 });

        Assert.Empty(ProbeConnections(factory));
        Assert.NotEqual(fakeNpgsqlDbType.Unknown, ParamFor(sc, "happy").NpgsqlDbType);
    }

    [Fact]
    public async Task TheProbeRunsOncePerTableAndContext()
    {
        var (context, factory) = Postgres();
        await using var _ = context;
        var gateway = new TableGateway<Row, int>(context);

        await gateway.CreateAsync(new Row { Id = 1 });
        await gateway.CreateAsync(new Row { Id = 2 });
        await new TableGateway<Row, int>(context).CreateAsync(new Row { Id = 3 });

        Assert.Single(factory.CreatedConnections.SelectMany(c => c.ExecutedReaderTexts), t => t == Probe);
    }

    [Fact]
    public async Task AFailedProbe_ChangesNothing()
    {
        var (context, factory) = Postgres(declare: false);
        await using var _ = context;
        factory.SetCommandFailure(Probe, new InvalidOperationException("permission denied"));
        var gateway = new TableGateway<Row, int>(context);

        Assert.True(await gateway.CreateAsync(new Row { Id = 1 }));

        using var sc = gateway.BuildCreate(new Row { Id = 2 });
        Assert.NotEqual(fakeNpgsqlDbType.Unknown, ParamFor(sc, "happy").NpgsqlDbType);
    }

    [Fact]
    public async Task ADialectThatNeedsNoDeclaredTypes_NeverProbes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        await using var context = new DatabaseContext("Data Source=x;EmulatedProduct=SqlServer", factory);

        await new TableGateway<Row, int>(context).CreateAsync(new Row { Id = 1 });

        Assert.DoesNotContain(factory.CreatedConnections.SelectMany(c => c.ExecutedReaderTexts), t => t.Contains("WHERE 1 = 0"));
    }
}
