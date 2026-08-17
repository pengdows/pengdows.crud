using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// <see cref="FirebirdDialect.GuidStorageMode"/> is an init-only, per-instance-configurable
/// property (independent of server version) that changes GUID wire format via
/// <c>SqlDialect.GuidFormat</c>. If <see cref="SqlDialect.CacheFingerprint"/> ever became the key
/// for a Firebird-consumed cache that bakes parameter construction into its cached artifact (as
/// the base <c>_insertBinders</c>/<c>_upsertBinders</c>/<c>_updateBinders</c>/
/// <c>_containersByDialect</c> caches do today, keyed by dialect instance instead), two Firebird
/// tenants on the identical server version but different <see cref="FirebirdDialect.GuidStorageMode"/>
/// would collapse onto one fingerprint and silently corrupt each other's GUID parameters — the
/// direct Firebird analogue of the MySQL upsert-version cache collision fixed for
/// <c>TableGateway</c>/<c>PrimaryKeyTableGateway</c> (see
/// <see cref="pengdows.crud.Tests.TableGatewayMultiTenantDialectCacheTests"/>). This locks the
/// precondition down so that future conversion is safe on day one.
/// </summary>
public class FirebirdCacheFingerprintTests
{
    [Fact]
    public void CacheFingerprint_DiffersByGuidStorageMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var logger = NullLogger.Instance;

        var binaryDialect = new FirebirdDialect(factory, logger)
        {
            GuidStorageMode = FirebirdGuidStorageMode.Binary
        };
        var stringDialect = new FirebirdDialect(factory, logger)
        {
            GuidStorageMode = FirebirdGuidStorageMode.String
        };

        Assert.NotEqual(binaryDialect.CacheFingerprint, stringDialect.CacheFingerprint);
    }

    [Fact]
    public void CacheFingerprint_SameForMatchingGuidStorageMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var logger = NullLogger.Instance;

        var dialectA = new FirebirdDialect(factory, logger)
        {
            GuidStorageMode = FirebirdGuidStorageMode.String
        };
        var dialectB = new FirebirdDialect(factory, logger)
        {
            GuidStorageMode = FirebirdGuidStorageMode.String
        };

        Assert.Equal(dialectA.CacheFingerprint, dialectB.CacheFingerprint);
    }
}
