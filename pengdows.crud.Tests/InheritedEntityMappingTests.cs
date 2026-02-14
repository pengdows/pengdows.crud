#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class InheritedEntityMappingTests
{
    [Fact]
    public void TypeMapRegistry_MapsBaseEntityColumns_WithExpectedDbTypes()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<ExternalIdentity>();

        Assert.Equal(DbType.Guid, info.Columns["id"].DbType);
        Assert.Equal(DbType.DateTime, info.Columns["created_on"].DbType);
        Assert.Equal(DbType.String, info.Columns["created_by"].DbType);
        Assert.Equal(DbType.DateTime, info.Columns["last_updated_on"].DbType);
        Assert.Equal(DbType.String, info.Columns["last_updated_by"].DbType);

        Assert.Equal(DbType.DateTime, info.Columns["last_used_on"].DbType);
        Assert.Equal(DbType.Guid, info.Columns["last_used_by"].DbType);
    }

    [Fact]
    public async Task BuildUpdateAsync_DoesNotBindGuidDbTypeToDateTimeValues()
    {
        var registry = new TypeMapRegistry();
        await using var context = new DatabaseContext(
            "Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer),
            registry);
        var gateway = new TableGateway<ExternalIdentity, Guid>(context, new StubAuditValueResolver("test-user"));

        var lastUsedOn = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var entity = new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            Provider = "provider",
            Issuer = "issuer",
            Subject = "subject",
            UserId = Guid.NewGuid(),
            Email = "user@example.com",
            DisplayName = "Test User",
            Claims = new Dictionary<string, string> { ["role"] = "admin" },
            LastUsedOn = lastUsedOn,
            LastUsedBy = Guid.NewGuid(),
            Version = 1
        };

        var sc = await gateway.BuildUpdateAsync(entity, false, context);
        var parameters = GetParameters(sc);

        Assert.DoesNotContain(parameters.Values,
            p => p.DbType == DbType.Guid && (p.Value is DateTime || p.Value is DateTimeOffset));

        var lastUsedOnParam = parameters.Values.Single(p => p.Value is DateTime dt && dt == lastUsedOn);
        Assert.Equal(DbType.DateTime, lastUsedOnParam.DbType);
    }

    private static IDictionary<string, DbParameter> GetParameters(ISqlContainer container)
    {
        var sqlContainer = Assert.IsType<SqlContainer>(container);
        var field = typeof(SqlContainer).GetField("_parameters", BindingFlags.Instance | BindingFlags.NonPublic);
        return (IDictionary<string, DbParameter>)field!.GetValue(sqlContainer)!;
    }

    private abstract class BaseEntity
    {
        [Id(writable: true)]
        [Column("id", DbType.Guid)]
        public Guid Id { get; set; } = Uuid7Optimized.NewUuid7();

        [Column("created_on", DbType.DateTime)]
        [CreatedOn]
        public DateTime CreatedOn { get; set; }

        [Column("created_by", DbType.String)]
        [CreatedBy]
        public string? CreatedBy { get; set; }

        [Column("last_updated_on", DbType.DateTime)]
        [LastUpdatedOn]
        public DateTime LastUpdatedOn { get; set; }

        [Column("last_updated_by", DbType.String)]
        [LastUpdatedBy]
        public string? LastUpdatedBy { get; set; }
    }

    [Table("external_identities", "openidentify")]
    private sealed class ExternalIdentity : BaseEntity
    {
        [PrimaryKey(1)]
        [Column("provider", DbType.String)]
        public string Provider { get; set; } = string.Empty;

        [PrimaryKey(2)]
        [Column("issuer", DbType.String)]
        public string Issuer { get; set; } = string.Empty;

        [PrimaryKey(3)]
        [Column("subject", DbType.String)]
        public string Subject { get; set; } = string.Empty;

        [Column("user_id", DbType.Guid)]
        public Guid UserId { get; set; }

        [Column("email", DbType.String)]
        public string? Email { get; set; }

        [Column("display_name", DbType.String)]
        public string? DisplayName { get; set; }

        [Column("claims", DbType.String)]
        [Json]
        public Dictionary<string, string> Claims { get; set; } = new();

        [Column("last_used_on", DbType.DateTime)]
        public DateTime? LastUsedOn { get; set; }

        [Column("last_used_by", DbType.Guid)]
        public Guid? LastUsedBy { get; set; }

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }
    }
}
