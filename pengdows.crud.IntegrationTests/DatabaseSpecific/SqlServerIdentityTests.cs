using System;
using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

[Collection("IntegrationTests")]
public class SqlServerIdentityTests : DatabaseTestBase
{
    public SqlServerIdentityTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return new[] { SupportedDatabase.SqlServer };
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var sql = @"
IF OBJECT_ID(N'[dbo].[user_info_temp]', 'U') IS NOT NULL
    DROP TABLE [dbo].[user_info_temp];

CREATE TABLE [dbo].[user_info_temp] (
    [id] INT IDENTITY(1,1) NOT NULL,
    [user_id] NVARCHAR(64) NOT NULL,
    [user_pass] NVARCHAR(500) NOT NULL,
    [role] NVARCHAR(50) NOT NULL,
    [mobile] NVARCHAR(100) NULL,
    [daily_update] BIT NULL,
    [active] BIT NULL,
    [login_alert] BIT NULL,
    [receive_otp] BIT NULL,
    CONSTRAINT PK_user_info_temp PRIMARY KEY ([user_id])
);";

        await using var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task CreateAsync_PopulatesIdentityIdAndStoresRow()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.SqlServer, async context =>
        {
            context.TypeMapRegistry.Register<UserInfoEntity>();
            var helper = new EntityHelper<UserInfoEntity, int>(context);

            var entity = new UserInfoEntity
            {
                Username = $"integration-{Guid.NewGuid():N}",
                Password = "secret",
                Role = "Admin",
                Mobile = "+15550000000",
                IsDailyUpdate = false,
                IsActive = true,
                IsLoginAlert = false,
                IsOtpReceived = false
            };

            var created = await helper.CreateAsync(entity, context);
            Assert.True(created, "INSERT should report success");
            Assert.True(entity.Id > 0, "Identity column should be populated via OUTPUT clause");

            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Username, retrieved!.Username);
            Assert.Equal(entity.Role, retrieved.Role);

            await using var verify = context.CreateSqlContainer(@"
SELECT COUNT(1)
FROM [dbo].[user_info_temp]
WHERE [user_id] = ");
            verify.Query.Append(verify.MakeParameterName("p0"));
            verify.AddParameterWithValue("p0", DbType.String, entity.Username);

            var count = Convert.ToInt32(await verify.ExecuteScalarAsync<int>());
            Assert.Equal(1, count);
        });
    }

    [Table("user_info_temp")]
    private class UserInfoEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey]
        [Column("user_id", DbType.String)]
        public string Username { get; set; } = string.Empty;

        [Column("user_pass", DbType.String)]
        public string Password { get; set; } = string.Empty;

        [Column("role", DbType.String)]
        public string Role { get; set; } = string.Empty;

        [Column("mobile", DbType.String)]
        public string? Mobile { get; set; }

        [Column("daily_update", DbType.Boolean)]
        public bool? IsDailyUpdate { get; set; }

        [Column("active", DbType.Boolean)]
        public bool? IsActive { get; set; }

        [Column("login_alert", DbType.Boolean)]
        public bool? IsLoginAlert { get; set; }

        [Column("receive_otp", DbType.Boolean)]
        public bool? IsOtpReceived { get; set; }
    }
}
