using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;
using Xunit.Abstractions;
using Sample = pengdows.crud.Tests.typesystem.TypeCompletenessTests.Sample;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Every CLR type the library reads, on every supported database, hydrated through both real mapper paths: the
/// gateway's compiled mapper (TableGateway.LoadListAsync) and DataReaderMapper's plan, each over a reader that returns
/// what that database's dialect binds for the value, with that database's coercion options. TypeCompletenessTests
/// proves the bind and the conversion rule; this proves the hydration paths agree with it, for every type on every
/// database, so a per-database optimization of a read can't change what any type reads as.
/// </summary>
[Collection("TypeRegistry")]
public class HydrationMatrixTests
{
    private readonly ITestOutputHelper _output;

    public HydrationMatrixTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static DatabaseContext Context(SupportedDatabase db, out fakeDbConnection connection)
    {
        var factory = new fakeDbFactory(db);
        connection = new fakeDbConnection
        {
            EmulatedProduct = db,
            ConnectionString = $"Data Source=test;EmulatedProduct={db}"
        };
        factory.Connections.Add(connection);

        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        }, factory, NullLoggerFactory.Instance);
    }

    // What a provider hands back for a value the dialect bound: JSON as its text, an enum stored as text by its name
    // (the gateway binds the name), the bound value itself for everything else.
    private static object ProviderShape(object bound, DbType dbType) => bound switch
    {
        System.Text.Json.JsonDocument document => document.RootElement.GetRawText(),
        System.Text.Json.JsonElement element => element.GetRawText(),
        pengdows.crud.types.valueobjects.JsonValue json => json.AsString(),
        Enum member when dbType == DbType.String => member.ToString(),
        Enum member => Convert.ChangeType(member, Enum.GetUnderlyingType(member.GetType())),
        _ => bound
    };

    // What the database's provider would hand the mapper: the dialect-bound value, in the shape a provider returns it.
    private static fakeDbDataReader ReaderOf(SqlDialect dialect, Sample sample)
    {
        var bound = ProviderShape(dialect.CreateDbParameter("p", sample.DbType, sample.Value()).Value!, sample.DbType);
        return new fakeDbDataReader(new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase) { ["id"] = 1, ["v"] = bound }
        })
        {
            Columns = [new fakeDbColumn("id", typeof(int), "INTEGER"), new fakeDbColumn("v", bound.GetType(), "X")]
        };
    }

    private static async Task<object?> ThroughTheGateway<TRow, T>(DatabaseContext ctx, fakeDbConnection connection,
        SqlDialect dialect, Sample sample) where TRow : class, new()
    {
        connection.EnqueueReaderResult(ReaderOf(dialect, sample));
        var gateway = new TableGateway<TRow, int>(ctx);
        await using var container = ctx.CreateSqlContainer("SELECT id, v FROM matrix");
        var rows = await gateway.LoadListAsync(container);
        return typeof(TRow).GetProperty("V")!.GetValue(Assert.Single(rows));
    }

    private static async Task<object?> ThroughThePlan<T>(DatabaseContext ctx, fakeDbConnection connection,
        SqlDialect dialect, Sample sample)
    {
        connection.EnqueueReaderResult(ReaderOf(dialect, sample));
        await using var container = ctx.CreateSqlContainer("SELECT id, v FROM matrix");
        await using var reader = await container.ExecuteReaderAsync();
        var rows = await DataReaderMapper.LoadAsync<MatrixValue<T>>(reader, new MapperOptions { Strict = true });
        return Assert.Single(rows).V;
    }

    private static readonly MethodInfo Gateway =
        typeof(HydrationMatrixTests).GetMethod(nameof(ThroughTheGateway), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo Plan =
        typeof(HydrationMatrixTests).GetMethod(nameof(ThroughThePlan), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<object?> Run(MethodInfo open, Type[] args, params object[] arguments) =>
        await (dynamic)open.MakeGenericMethod(args).Invoke(null, arguments)!;

    [Fact]
    public async Task EveryTypeOnEveryDatabase_HydratesThroughBothMapperPaths_AsTheConversionMatrixSays()
    {
        var failures = new List<string>();
        var tested = 0;

        foreach (var db in TypeCompletenessTests.Products())
        {
            using var ctx = Context(db, out var connection);
            var dialect = (SqlDialect)ctx.Dialect;

            foreach (var sample in TypeCompletenessTests.Samples())
            {
                // The pairs the conversion matrix says convert; the others have a recorded reason and are
                // verified live (docs/type-support-matrix.md).
                if (TypeCompletenessTests.Outcome(sample, dialect) != "ok")
                {
                    continue;
                }

                var expected = TypeCompletenessTests.Canonical(sample.Value());
                var row = HydrationMatrixRows.ByDbType[sample.DbType].MakeGenericType(sample.Clr);

                foreach (var (path, run) in new (string, Func<Task<object?>>)[]
                         {
                             ("gateway", () => Run(Gateway, new[] { row, sample.Clr }, ctx, connection, dialect, sample)),
                             ("plan", () => Run(Plan, new[] { sample.Clr }, ctx, connection, dialect, sample))
                         })
                {
                    tested++;
                    try
                    {
                        var actual = TypeCompletenessTests.Canonical(await run());
                        if (actual != expected)
                        {
                            failures.Add($"{db}/{sample.Label}/{path}: expected {expected}, read {actual}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{db}/{sample.Label}/{path}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                    }
                }
            }
        }

        _output.WriteLine($"{tested} hydrations across {TypeCompletenessTests.Products().Count()} databases");
        foreach (var f in failures)
        {
            _output.WriteLine("  FAIL " + f);
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {tested} hydrations differ:\n" + string.Join("\n", failures));
    }
}
