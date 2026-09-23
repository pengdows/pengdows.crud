using System.Data.OleDb;
using pengdows.crud;
using pengdows.crud.@internal;
using pengdows.crud.enums;

namespace testbed.Access;

/// <summary>
/// No overrides needed for <c>TestProvider.CreateTable</c> itself beyond the shared per-provider
/// type-name lookups in <c>TestProvider.cs</c> (<c>GetIntType</c>/<c>GetLongType</c>/
/// <c>GetBooleanType</c>/<c>GetTextType</c> each gained an <c>Access</c> case: <c>LONG</c> for
/// both int and long columns, <c>YESNO</c> for boolean, <c>TEXT(n)</c> up to 255 characters and
/// <c>MEMO</c> beyond that — values carried over from pengdows.crud 3.0's live-verified
/// <c>AccessTestProvider.cs</c>, not independently re-verified in this session). Every container
/// this test runs against is a brand-new, freshly-created <c>.accdb</c> file (see
/// <see cref="AccessTestContainer"/>), so there is no stale-state hazard requiring a
/// provider-specific pre-drop override, unlike InterBase's persistent, externally-managed
/// database.
/// <para>
/// <see cref="RunAdditionalTestsAsync"/> only adds the one Access-specific regression check
/// below — CRUD round-trip, constraint-violation, and upsert-not-supported coverage are already
/// exercised generically by the base <c>TestProvider</c> flow (<c>TestRowRoundTrip</c>,
/// <c>TestErrorMapping</c>, <c>TestUpsertCapability</c>, etc.), unlike pengdows.crud 3.0's
/// equivalent file, which predates that generic coverage and called dedicated
/// <c>TestCrudRoundTripAsync</c>/<c>TestUniqueConstraintViolationAsync</c>/
/// <c>TestUpsertIsNotSupportedAsync</c> helpers that don't exist on this branch.
/// </para>
/// </summary>
public class AccessTestProvider : TestProvider
{
    public AccessTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    protected override Task RunAdditionalTestsAsync()
    {
        TestOleDbFactoryAloneDoesNotImplyAccess();
        return Task.CompletedTask;
    }

    /// <summary>
    /// AccessDialect.cs's remarks explain WHY no <c>FactoryTypeTokens</c> entry was added for
    /// Access: <c>factory.GetType().FullName</c> is <c>"System.Data.OleDb.OleDbFactory"</c> for
    /// ANY OLE DB provider (SQLOLEDB for SQL Server, OraOLEDB for Oracle, etc.), so matching on it
    /// would misdetect every other OLE DB connection as Access. This check proves that reasoning
    /// against the REAL <see cref="OleDbFactory"/> type (not a same-named fakeDb stand-in), using
    /// the real internal <see cref="DatabaseDetectionService.DetectFromFactory"/> pengdows.crud
    /// exposes to this project via InternalsVisibleTo — the one place safe to do this at all,
    /// since pengdows.crud.Tests (which runs in CI on ubuntu-latest) cannot reference
    /// System.Data.OleDb without a real cross-platform risk this project doesn't have (opt-in,
    /// Windows-only already).
    /// </summary>
    private void TestOleDbFactoryAloneDoesNotImplyAccess()
    {
        var detected = DatabaseDetectionService.DetectFromFactory(OleDbFactory.Instance);
        if (detected == SupportedDatabase.Unknown)
        {
            CheckOk(
                "  [Access.OleDbFactoryAloneIsNotAccess] DetectFromFactory(OleDbFactory.Instance) correctly returns Unknown, not Access — confirms factory-type-name alone (shared by every OLE DB provider) cannot misdetect an unrelated OLE DB connection as Access; only a live schema probe reporting \"MS Jet\" can.");
        }
        else
        {
            throw new Exception($"DetectFromFactory(OleDbFactory.Instance) returned {detected}, expected Unknown");
        }
    }
}
