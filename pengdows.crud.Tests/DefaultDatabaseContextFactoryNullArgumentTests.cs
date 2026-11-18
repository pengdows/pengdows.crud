using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests
{
    public class DefaultDatabaseContextFactoryNullArgumentTests
    {
        private static readonly DatabaseContextConfiguration Configuration = new()
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        [Fact]
        public async Task CreateAsync_NullConfiguration_Throws()
        {
            var factory = new DefaultDatabaseContextFactory();

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                factory.CreateAsync(null!, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance));
        }

        [Fact]
        public async Task CreateAsync_NullProviderFactory_Throws()
        {
            var factory = new DefaultDatabaseContextFactory();

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                factory.CreateAsync(Configuration, null!, NullLoggerFactory.Instance));
        }

        [Fact]
        public void Create_NullConfiguration_Throws()
        {
            var factory = new DefaultDatabaseContextFactory();

            Assert.Throws<ArgumentNullException>(() =>
                factory.Create(null!, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance));
        }
    }
}
