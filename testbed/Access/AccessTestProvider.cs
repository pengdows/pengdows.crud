using System.Data.OleDb;
using pengdows.crud;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.exceptions;

namespace testbed.Access;

/// <summary>
/// No overrides needed for <see cref="TestProvider.CreateTable"/> itself beyond the shared
/// per-provider type-name lookups (see <c>TestProvider.cs</c>'s <c>GetLongType</c>/
/// <c>GetIntType</c>/<c>GetBooleanType</c>/<c>GetTextType</c> — each has an <c>Access</c> case,
/// confirmed live via a real <c>CREATE TABLE</c> + <c>INSERT</c> + <c>SELECT</c> round trip
/// against a real <c>.accdb</c>: <c>LONG</c> for both int and long columns, <c>YESNO</c> for
/// boolean, <c>TEXT(n)</c> up to 255 characters and <c>MEMO</c> beyond that, <c>DATETIME</c>
/// unchanged from the base default). Every container this test runs against is a brand-new,
/// freshly-created <c>.accdb</c> file (see <see cref="AccessTestContainer"/>), so the base
/// <c>CreateTable</c>'s <c>DROP TABLE IF EXISTS</c> pre-step never actually needs to drop
/// anything — unlike InterBase's persistent, externally-managed database, there is no stale-state
/// hazard here requiring a provider-specific pre-drop override.
/// <para>
/// <see cref="RunAdditionalTestsAsync"/> IS overridden — per <see cref="TestProvider"/>'s own
/// class remarks, CRUD round-trips normally belong in <c>pengdows.crud.IntegrationTests</c>
/// instead of here, but Access categorically cannot plug into that project's shared
/// <c>IntegrationTestFixture</c> (Windows-only, no Docker image, an ADOX-created <c>.accdb</c>
/// rather than a Testcontainers-managed connection string) — this is exactly the "still genuinely
/// needs the live container itself" case that class's own doc comment carves out as the
/// legitimate reason to override this method.
/// </para>
/// </summary>
public class AccessTestProvider : TestProvider
{
    public AccessTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    protected override async Task RunAdditionalTestsAsync()
    {
        await TestCrudRoundTripAsync();
        await TestUniqueConstraintViolationAsync();
        await TestUpsertIsNotSupportedAsync();
        TestOleDbFactoryAloneDoesNotImplyAccess();
    }

    /// <summary>
    /// AccessDialect.cs's remarks explain WHY no <c>FactoryTypeTokens</c> entry was added for
    /// Access: <c>factory.GetType().FullName</c> is <c>"System.Data.OleDb.OleDbFactory"</c> for
    /// ANY OLE DB provider (SQLOLEDB for SQL Server, OraOLEDB for Oracle, etc.), so matching on it
    /// would misdetect every other OLE DB connection as Access. This check proves that reasoning
    /// against the REAL <see cref="OleDbFactory"/> type (not a same-named stand-in), using the
    /// real internal <see cref="DatabaseDetectionService.DetectFromFactory"/> pengdows.crud
    /// exposes to this project via InternalsVisibleTo — the one place safe to do this at all,
    /// since pengdows.crud.Tests (which runs in CI on ubuntu-latest) cannot reference
    /// System.Data.OleDb without a real cross-platform risk this project doesn't have (opt-in,
    /// Windows-only already).
    /// </summary>
    private void TestOleDbFactoryAloneDoesNotImplyAccess()
    {
        const string checkName = "Access.OleDbFactoryAloneIsNotAccess";
        try
        {
            var detected = DatabaseDetectionService.DetectFromFactory(OleDbFactory.Instance);
            if (detected == SupportedDatabase.Unknown)
            {
                CheckOk(checkName,
                    "  [Access.OleDbFactoryAloneIsNotAccess] DetectFromFactory(OleDbFactory.Instance) correctly returns Unknown, not Access — confirms factory-type-name alone (shared by every OLE DB provider) cannot misdetect an unrelated OLE DB connection as Access; only a live schema probe reporting \"MS Jet\" can.");
            }
            else
            {
                CheckFail(checkName, $"DetectFromFactory(OleDbFactory.Instance) returned {detected}, expected Unknown");
            }
        }
        catch (Exception ex)
        {
            CheckFail(checkName, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Access has no MERGE/ON CONFLICT/ON DUPLICATE KEY of any kind — confirmed there is no
    /// server-side upsert mechanism at all (not just unimplemented in this dialect).
    /// TableGateway.BuildUpsert requires one of SupportsMerge/SupportsInsertOnConflict/
    /// SupportsOnDuplicateKey to be true or it throws NotSupportedException; AccessDialect
    /// inherits all three as false, so UpsertAsync is expected to throw. This was never actually
    /// exercised against a real Access engine until now — confirming it here rather than leaving
    /// it as an unverified consequence of three inherited false flags.
    /// </summary>
    private async Task TestUpsertIsNotSupportedAsync()
    {
        const string checkName = "Access.UpsertNotSupported";
        try
        {
            var entity = new TestTable
            {
                Id = 3,
                Name = NameEnum.Test,
                Description = "upsert probe",
                Value = 1,
                IsActive = true
            };

            try
            {
                await _helper.UpsertAsync(entity);
                CheckFail(checkName, "UpsertAsync did not throw for Access, which has no MERGE/ON CONFLICT/ON DUPLICATE KEY mechanism");
            }
            catch (NotSupportedException)
            {
                CheckOk(checkName,
                    "  [Access.UpsertNotSupported] UpsertAsync correctly throws NotSupportedException against a real .accdb — confirmed live, not just inferred from three inherited false capability flags");
            }
        }
        catch (Exception ex)
        {
            CheckFail(checkName, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await _helper.DeleteAsync(3L);
        }
    }

    private async Task TestCrudRoundTripAsync()
    {
        const string checkName = "Access.CrudRoundTrip";
        try
        {
            var entity = new TestTable
            {
                Id = 1,
                Name = NameEnum.Test,
                Description = "initial description",
                Value = 42,
                IsActive = true
            };

            var created = await _helper.CreateAsync(entity);
            if (!created)
            {
                CheckFail(checkName, "CreateAsync returned false");
                return;
            }

            var retrieved = await _helper.RetrieveOneAsync(1L);
            if (retrieved is null)
            {
                CheckFail(checkName, "RetrieveOneAsync returned null immediately after CreateAsync");
                return;
            }

            if (retrieved.Description != "initial description" || retrieved.Value != 42 || retrieved.IsActive != true)
            {
                CheckFail(checkName,
                    $"retrieved row does not match inserted values: description='{retrieved.Description}', value={retrieved.Value}, isActive={retrieved.IsActive}");
                return;
            }

            retrieved.Description = "updated description";
            retrieved.Value = 99;
            retrieved.IsActive = false;
            var updatedCount = await _helper.UpdateAsync(retrieved);
            if (updatedCount != 1)
            {
                CheckFail(checkName, $"UpdateAsync affected {updatedCount} rows, expected 1");
                return;
            }

            var afterUpdate = await _helper.RetrieveOneAsync(1L);
            if (afterUpdate is null || afterUpdate.Description != "updated description" || afterUpdate.Value != 99 || afterUpdate.IsActive)
            {
                CheckFail(checkName,
                    $"post-update row does not reflect the update: description='{afterUpdate?.Description}', value={afterUpdate?.Value}, isActive={afterUpdate?.IsActive}");
                return;
            }

            var deletedCount = await _helper.DeleteAsync(1L);
            if (deletedCount != 1)
            {
                CheckFail(checkName, $"DeleteAsync affected {deletedCount} rows, expected 1");
                return;
            }

            var afterDelete = await _helper.RetrieveOneAsync(1L);
            if (afterDelete is not null)
            {
                CheckFail(checkName, "row still present after DeleteAsync");
                return;
            }

            CheckOk(checkName, "  [Access.CrudRoundTrip] Create -> Retrieve -> Update -> Retrieve -> Delete -> Retrieve, all confirmed against a real .accdb");
        }
        catch (Exception ex)
        {
            CheckFail(checkName, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task TestUniqueConstraintViolationAsync()
    {
        const string checkName = "Access.UniqueConstraintViolation";
        try
        {
            var first = new TestTable
            {
                Id = 2,
                Name = NameEnum.Test,
                Description = "first",
                Value = 1,
                IsActive = true
            };
            var second = new TestTable
            {
                Id = 2,
                Name = NameEnum.Test2,
                Description = "second (duplicate id)",
                Value = 2,
                IsActive = true
            };

            if (!await _helper.CreateAsync(first))
            {
                CheckFail(checkName, "setup CreateAsync (id=2) returned false");
                return;
            }

            try
            {
                await _helper.CreateAsync(second);
                CheckFail(checkName, "CreateAsync with a duplicate primary key did not throw");
            }
            catch (UniqueConstraintViolationException)
            {
                CheckOk(checkName,
                    "  [Access.UniqueConstraintViolation] A duplicate-primary-key INSERT against a real .accdb threw UniqueConstraintViolationException, confirming AccessExceptionTranslator/AccessDialect.IsUniqueViolation classify the real live OleDbException correctly, not just the synthetic message-text fixtures in AccessTranslatorTests.cs");
            }
            finally
            {
                await _helper.DeleteAsync(2L);
            }
        }
        catch (Exception ex)
        {
            CheckFail(checkName, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
