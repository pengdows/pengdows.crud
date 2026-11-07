using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.typesystem
{
    /// <summary>One column of a generic row type: the mapper reads its property as T.</summary>
    public class PinHolder<T>
    {
        [Column("v", DbType.Object)]
        public T V { get; set; } = default!;
    }

    /// <summary>The same column, on an entity a gateway can read (it needs an [Id]).</summary>
    [Table("pin_row")]
    public class PinEntity<T>
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("v", DbType.Object)]
        public T V { get; set; } = default!;
    }

    /// <summary>
    /// Pins what the two read paths a developer uses return for each stored value and target type on each database: the
    /// DataReaderMapper (for POCOs) and a TableGateway (for entities). The helper-level read is pinned by
    /// TypeSystemCharacterizationTests; this one pins the mapper paths, where the read decisions were duplicated. A change
    /// to either path must leave this file unchanged, or be a deliberate, reviewed re-pin (PIN_TYPES=1).
    /// </summary>
    public class TypeSystemMapperCharacterizationTests
    {
        [Fact]
        public async Task MapperReads_MatchThePinnedResults() =>
            TypeSystemCharacterizationTests.AssertPinnedFile(PinnedPath("TypeSystemMapperReads.txt"), await DescribeMapperReads());

        private static string PinnedPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, name);

        internal static async Task<string> DescribeMapperReads()
        {
            var products = TypeSystemCharacterizationTests.Products().ToArray();
            var text = new StringBuilder();
            // A NULL stored value for each target type too: the two mappers handle NULL on their own paths.
            var samples = TypeSystemCharacterizationTests.ReadSamples()
                .Concat(TypeSystemCharacterizationTests.ReadSamples().Select(s => s.Target).Distinct()
                    .Select(target => ((object)DBNull.Value, target)))
                .ToList();
            foreach (var (raw, target) in samples)
            {
                var mapper = new Dictionary<string, List<SupportedDatabase>>(StringComparer.Ordinal);
                var gateway = new Dictionary<string, List<SupportedDatabase>>(StringComparer.Ordinal);
                foreach (var product in products)
                {
                    var mapperResult = await Outcome(() => MapperOne(product, raw, target));
                    var gatewayResult = await Outcome(() => GatewayOne(product, raw, target));
                    Add(mapper, mapperResult, product);
                    Add(gateway, gatewayResult, product);
                }

                text.Append(TypeSystemCharacterizationTests.Show(raw)).Append(" -> ").Append(target.Name);
                if (Nullable.GetUnderlyingType(target) != null)
                {
                    text.Append('?');
                }

                AppendPath(text, "mapper", mapper);
                AppendPath(text, "gateway", gateway);
                text.AppendLine();
            }

            return text.ToString();
        }

        private static void Add(Dictionary<string, List<SupportedDatabase>> results, string result, SupportedDatabase product)
        {
            if (!results.TryGetValue(result, out var list))
            {
                results[result] = list = new List<SupportedDatabase>();
            }

            list.Add(product);
        }

        private static void AppendPath(StringBuilder text, string path, Dictionary<string, List<SupportedDatabase>> results)
        {
            text.Append(" [").Append(path).Append("] ");
            if (results.Count == 1)
            {
                text.Append(results.Keys.Single());
                return;
            }

            text.Append(string.Join(" | ", results.Select(kv => kv.Key + " on " + string.Join(",", kv.Value))));
        }

        private static async Task<string> Outcome(Func<Task<string>> f)
        {
            try
            {
                return await f();
            }
            catch (Exception ex)
            {
                return "throws " + ex.GetType().Name;
            }
        }

        private static async Task<string> MapperOne(SupportedDatabase product, object raw, Type target)
        {
            var method = typeof(TypeSystemMapperCharacterizationTests).GetMethod(nameof(MapperTyped),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(target);
            return await (Task<string>)method.Invoke(null, new object[] { product, raw })!;
        }

        private static async Task<string> GatewayOne(SupportedDatabase product, object raw, Type target)
        {
            var method = typeof(TypeSystemMapperCharacterizationTests).GetMethod(nameof(GatewayTyped),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(target);
            return await (Task<string>)method.Invoke(null, new object[] { product, raw })!;
        }

        private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase product, object raw)
        {
            var factory = new fakeDbFactory(product);
            // A provider that learns its declared column types first (Informix) holds one connection for that probe while the
            // read takes another, and the factory makes a new one past the list. Every connection carries the row, so the read
            // finds it on whichever one it gets.
            var connections = new List<fakeDbConnection>();
            for (var i = 0; i < 4; i++)
            {
                var connection = new fakeDbConnection { EmulatedProduct = product };
                connections.Add(connection);
                factory.Connections.Add(connection);
            }
            var exec = connections[1];
            var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = $"Server=x;Database=y;EmulatedProduct={product}",
                DbMode = DbMode.Standard
            }, factory);
            foreach (var connection in connections)
            {
                for (var i = 0; i < 2; i++)
                {
                    connection.EnqueueReaderResult(new fakeDbDataReader(new[]
                    {
                        new Dictionary<string, object> { ["id"] = 1, ["v"] = raw }
                    }));
                }
            }
            return (context, exec);
        }

        private static async Task<string> MapperTyped<T>(SupportedDatabase product, object raw)
        {
            var (context, _) = Context(product, raw);
            await using var ctx = context;
            var sc = ctx.CreateSqlContainer("SELECT v FROM pin_row");
            await using var reader = await sc.ExecuteReaderAsync();
            var rows = await DataReaderMapper.LoadAsync<PinHolder<T>>(reader, MapperOptions.Default);
            return TypeSystemCharacterizationTests.Show(rows[0].V);
        }

        private static async Task<string> GatewayTyped<T>(SupportedDatabase product, object raw)
        {
            var (context, _) = Context(product, raw);
            await using var ctx = context;
            var gateway = new TableGateway<PinEntity<T>, int>(ctx);
            var row = await gateway.RetrieveOneAsync(1);
            return TypeSystemCharacterizationTests.Show(row!.V);
        }
    }
}
