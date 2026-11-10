using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>An application type the library has no mapping for: a money amount kept as minor units.</summary>
    public sealed class MinorUnits
    {
        public long Value { get; init; }
    }

    /// <summary>The application's escape hatch: how one unknown type is written to and read from a database.</summary>
    public sealed class MinorUnitsConverter : IProviderValueConverter<MinorUnits>
    {
        public object? ToProvider(MinorUnits value, SupportedDatabase provider) => value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public MinorUnits FromProvider(object value, SupportedDatabase provider) =>
            new() { Value = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) };
    }

    [Table("custom_money")]
    public class CustomMoneyEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("amount", DbType.String)]
        public MinorUnits? Amount { get; set; }
    }

    /// <summary>
    /// An application can register a converter for a type the library does not map, so an unknown type needs no change to
    /// the library. The library owns the conversion once it is registered: it writes the converter's value and reads the
    /// column back through it. A null stays null.
    /// </summary>
    public class CustomValueConverterTests
    {
        static CustomValueConverterTests()
        {
            ProviderValueConverters.Register(new MinorUnitsConverter());
        }

        [Fact]
        public void AnUnknownTypeIsWrittenThroughTheRegisteredConverter()
        {
            using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
            var gateway = new TableGateway<CustomMoneyEntity, int>(context);

            using var sc = gateway.BuildCreate(new CustomMoneyEntity { Id = 1, Amount = new MinorUnits { Value = 1250 } });

            Assert.Contains("1250", Enumerable(sc));
        }

        [Fact]
        public async Task AnUnknownTypeIsReadThroughTheRegisteredConverter()
        {
            var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
            factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
            var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
            factory.Connections.Add(exec);
            exec.EnqueueReaderResult(new fakeDbDataReader(new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["amount"] = "1250" }
            }));
            await using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
                DbMode = DbMode.Standard
            }, factory);
            var gateway = new TableGateway<CustomMoneyEntity, int>(context);

            var row = await gateway.RetrieveOneAsync(1);

            Assert.Equal(1250, row!.Amount!.Value);
        }

        [Fact]
        public void ANullUnknownTypeStaysNull()
        {
            using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
            var gateway = new TableGateway<CustomMoneyEntity, int>(context);

            using var sc = gateway.BuildCreate(new CustomMoneyEntity { Id = 1, Amount = null });

            Assert.NotNull(sc);
        }

        [Fact]
        public void RegisteringTheSameTypeTwiceIsRefused()
        {
            Assert.Throws<InvalidOperationException>(() => ProviderValueConverters.Register(new MinorUnitsConverter()));
        }

        private static string Enumerable(pengdows.crud.ISqlContainer sc)
        {
            var parts = new List<string>();
            for (var i = 0; i < sc.ParameterCount; i++)
            {
                parts.Add(sc.GetParameterValue("i" + i)?.ToString() ?? string.Empty);
            }

            return string.Join("|", parts);
        }
    }
}
