using System;
using System.IO;
using Microsoft.Data.Sqlite;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// G6b (CONCURRENCY.md) carried an open question: "whether removing the Pooling key, rather than
/// setting Pooling=False, is the intended behavior." That question turned out to be moot, and
/// this is the test that found out why -- the caller cannot choose: SingleWriter unconditionally
/// strips a `Pooling` key from the connection string if one is present
/// (<c>ConnectionPoolingConfiguration.StripPoolingSetting</c>, keyed off
/// <c>DbConnectionStringBuilder.ContainsKey</c>, not off its value), so an explicit
/// <c>Pooling=False</c> supplied by the caller never reaches the real provider.
///
/// Neither half of this is reachable from fakeDb: fakeDb has no real connection-string parser
/// wired to <c>ConnectionPoolingConfiguration</c>-level stripping behavior in a way that would
/// expose it, and it models no real OS file handle or real provider connection pool at all -- so
/// the physical-handle consequence below needs the real provider. Observing the native handle
/// uses /proc/self/fd, the only reliable black-box signal that doesn't depend on
/// Microsoft.Data.Sqlite internals outside its public contract -- Linux-only, skipped elsewhere.
/// </summary>
public class SingleWriterPoolingBehaviorTests
{
    [SkippableFact]
    public void SqliteConnectionStringBuilder_RecognizesPooling_DefaultTrue()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");

        var builder = new SqliteConnectionStringBuilder();

        // Resolves the G6b Assumption: Microsoft.Data.Sqlite does define a real Pooling keyword
        // and it defaults to true.
        Assert.True(builder.Pooling);

        builder.Pooling = false;
        Assert.Contains("Pooling=False", builder.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void SingleWriter_StripsAnExplicitPoolingFalse_TheCallerCannotDisablePooling()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");

        // Deliberately no "pooling" substring anywhere in the temp file name itself -- the naive
        // version of this test asserted on the raw connection string and matched its own
        // "pengdows.pooling.*" file path instead of the actual Pooling= key.
        var dbFilePath = Path.Combine(Path.GetTempPath(), $"pengdows.strip-key-check.{Guid.NewGuid():N}.sqlite");
        try
        {
            var cfg = new DatabaseContextConfiguration
            {
                // The caller explicitly asks for Pooling=False...
                ConnectionString = $"Data Source={dbFilePath};Pooling=False",
                DbMode = DbMode.SingleWriter,
                ReadWriteMode = ReadWriteMode.ReadWrite
            };

            using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);

            // ...and SingleWriter removes it anyway: StripPoolingSetting fires whenever the key is
            // *present*, regardless of its value, so the real connection string the provider sees
            // has no Pooling keyword at all -- identical, for pooling purposes, to never having
            // specified it. Parsed via the same plain DbConnectionStringBuilder the library itself
            // uses for this check, not a raw substring search (which would also match "Pooling"
            // appearing inside some other value, such as a file path).
            var parsed = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = ctx.RawConnectionString };
            Assert.False(parsed.ContainsKey("Pooling"),
                $"Expected the Pooling key to be stripped; raw connection string was: {ctx.RawConnectionString}");
        }
        finally
        {
            TryDelete(dbFilePath);
        }
    }

    [SkippableFact]
    public void SingleWriter_WithOrWithoutAnExplicitPoolingFalse_LeavesTheSameFileHandlesOpenAfterDispose()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");
        SkipIfProcSelfFdUnavailable();

        // Both connection strings produce the identical real one after SingleWriter's stripping
        // (proven above), so this is a single scenario repeated under both spellings, not two
        // different behaviors to compare.
        AssertHandlesRemainOpenAfterFullDispose(explicitPoolingFalse: false);
        AssertHandlesRemainOpenAfterFullDispose(explicitPoolingFalse: true);
    }

    private static void AssertHandlesRemainOpenAfterFullDispose(bool explicitPoolingFalse)
    {
        var dbFilePath = Path.Combine(Path.GetTempPath(),
            $"pengdows.pooling.{(explicitPoolingFalse ? "explicit" : "default")}.{Guid.NewGuid():N}.sqlite");
        try
        {
            SqliteConnection.ClearAllPools();

            var connectionString = explicitPoolingFalse
                ? $"Data Source={dbFilePath};Pooling=False"
                : $"Data Source={dbFilePath}";

            var cfg = new DatabaseContextConfiguration
            {
                ConnectionString = connectionString,
                DbMode = DbMode.SingleWriter,
                ReadWriteMode = ReadWriteMode.ReadWrite
            };

            var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);
            using (var create = ctx.CreateSqlContainer("CREATE TABLE pooling_probe (id INTEGER PRIMARY KEY)"))
            {
                create.ExecuteNonQueryAsync().AsTask().GetAwaiter().GetResult();
            }

            using (var insert = ctx.CreateSqlContainer("INSERT INTO pooling_probe (id) VALUES (1)"))
            {
                insert.ExecuteNonQueryAsync().AsTask().GetAwaiter().GetResult();
            }

            // Dispose the DatabaseContext itself too, not just the two checkouts above: the fair
            // comparison is "every pengdows.crud-owned object is gone," not "the per-operation
            // checkout is gone but the context is still alive." G6b's own claim ("the library
            // holds no checked-out writer connection") is about the checkout; this measures the
            // *provider's* pool underneath, which outlives both.
            ctx.Dispose();

            var openFds = CountOpenFileDescriptorsFor(dbFilePath);

            Assert.True(openFds > 0,
                $"Expected the provider's connection pool to keep the SQLite file handle open " +
                $"after every pengdows.crud-owned object was disposed (explicitPoolingFalse: " +
                $"{explicitPoolingFalse}). If this reads 0, either Microsoft.Data.Sqlite's " +
                "pooling default has changed, or SingleWriter has stopped stripping the Pooling " +
                "key -- either way G6b's physical-connection note needs revisiting.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbFilePath);
        }
    }

    private static void SkipIfProcSelfFdUnavailable()
    {
        Skip.IfNot(Directory.Exists("/proc/self/fd"), "/proc/self/fd is not available on this platform.");
    }

    private static int CountOpenFileDescriptorsFor(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var count = 0;
        foreach (var fd in Directory.EnumerateFiles("/proc/self/fd"))
        {
            try
            {
                var target = new FileInfo(fd).LinkTarget;
                if (target != null && target.Contains(fileName, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            catch (IOException)
            {
                // The fd closed between enumeration and inspection -- not a match.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return count;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; an open handle (the very thing under test) can make this fail
            // on some platforms, but not Linux.
        }
    }
}
