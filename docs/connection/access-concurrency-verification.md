# Access (Jet/ACE) Concurrent-Write Verification Plan

## RESOLVED (2026-09-18)

This plan has been executed end-to-end on a real Windows machine (this repo's own dev machine),
against a real `.accdb` file via `System.Data.OleDb` and the installed Access Database Engine
Redistributable (ACE 16.0). All five phases below ran; the open question (Phase 3) is answered.
See `AccessDialect.cs`'s file-level AI SUMMARY for the authoritative, dated findings. Headline
results:

- **Phase 1** (re-confirm original claim): reproduced — 3-8/20 failures across three runs of raw
  `OleDb`, disjoint rows, same table.
- **Phase 2** (classic same-row conflict): reproduced, and worse — 9-14/20 failures, plus genuine
  lost-update anomalies (final counter value far below the expected 20 after all writers "commit").
- **Phase 3, the open question** (disjoint rows, two different tables): **fails** — 3-6/20
  failures, including failures in the table a given writer never touched itself. The conflict is
  **not table-scoped**; the original "same table" framing in `AccessDialect.cs` undersold it, if
  anything — it is at least file/page-scoped, matching (not narrower than) what was already
  written, unlike DuckDB's confirmed row-scoped behavior.
- **Phase 4** (the fix, through pengdows.crud): `DbMode.SingleWriter` — 20/20 succeeded, zero
  conflicts, three runs in a row.
- **Phase 5** (the hazard, through pengdows.crud's public API): this surfaced a **second, more
  fundamental bug** before it could even test the hazard — `DbMode.Standard` failed to open a
  connection at all (`OleDbException: Could not find installable ISAM`), on every attempt,
  including a single non-concurrent `CREATE TABLE` with zero writers. Root cause: `AccessDialect`
  never overrode `SupportsExternalPooling`/`PoolingSettingName`, so pengdows.crud was injecting an
  ADO.NET-style `Pooling=True` keyword the Jet/ACE driver doesn't recognize (confirmed: ANY
  unrecognized connection property throws this exact generic error). Fixed via TDD (see
  `AccessDialectTests.cs`). Once fixed, Phase 5 ran as originally planned — and a **naive**
  version (bare auto-committing `INSERT`s) misleadingly showed 20/20 success, because a
  single-statement write holds its lock too briefly to collide even under real concurrency. Redone
  fairly, with the same held-open-transaction contention pattern Phase 1/2 used (via
  `Context.BeginTransaction()`, not raw `OleDb`): `DbMode.Standard` reproduced the hazard clearly
  — 14-18/20 failures (`CommandTimeoutException`, "Could not update; currently locked.") — while
  the identical workload under `DbMode.SingleWriter` stayed at 0/20 failures.

A related gap found and fixed along the way, not originally in this plan's scope: `AccessDialect`
had no `ReadOnlyPoolDiscriminatorSettingName`, so its reader and writer connection strings were
identical, collapsing them into one shared physical `OleDb` pool (the same problem
`OracleDialect` already solved for ODP.NET). Fixed using `"Jet OLEDB:Database Locking Mode"` =>
`"1"` — confirmed live to be both a real, recognized ACE property and behaviorally inert (it's
ACE's own documented default), so it only differentiates the connection-string text without
changing real locking behavior.

Also added, prompted by a follow-up question during this investigation (not originally in this
plan): `GetReadOnlyConnectionParameter()` => `"Mode=Read"` — confirmed live to be a real,
ACE-recognized property that genuinely enforces read-only at the driver level (a write against it
fails with `"Operation must use an updateable query."`, correctly classified into
`ReadOnlyViolationException`), and confirmed (3/3 trials) not to block a concurrent writer on the
same file — unlike DuckDB, so no `ReadOnlyConnectionsCanBlockConcurrentWriters` override was
needed.

**Bottom line**: `DbMode.SingleWriter` is the only mode confirmed both correct and fully
concurrent for Access. `DbMode.Standard` now at least *works* (after the pooling fix) but is
confirmed unsafe for any workload that holds a transaction open across more than one round trip —
consistent with, and stronger evidence for, the same class of gap SQLite-specific libraries exist
to solve and general-purpose ORMs targeting client-server databases never had to.

The original, unresolved plan is preserved below for reference and methodology.

## Why this document exists

`AccessDialect.cs`'s file-level AI SUMMARY documents a "CONFIRMED live" finding: raw concurrent
`OleDb` writers against a real `.accdb` file produce `OleDbException: Could not update; currently
locked.`, and pengdows.crud's `DbMode.SingleWriter` governor prevents it. That finding is real, but
the script that produced it was never committed to the repo — it was ad hoc, run once in an earlier
session, and is now unrecoverable (confirmed by a full-repo/git-history search: nothing under
`testbed/Access/`, `pengdows.crud.IntegrationTests/`, or anywhere else in the tree exercises real
concurrency against Access; `AccessTestProvider.cs`'s `RunAdditionalTestsAsync()` only covers CRUD
round-trip, unique-constraint classification, and upsert-not-supported — no locking/concurrency
check at all).

Access is Windows-only (the ACE OLE DB provider and the ADOX COM interop used to create the
`.accdb` file both require it — see `AccessTestContainer.cs`), so this can't be re-verified or
turned into a committed regression test from this (Linux) environment. This document is the test
plan: what to run, what each result would mean, and what to update afterward.

It mirrors the methodology that was just used to verify the equivalent DuckDB claim live (see
`DuckDbDialect.cs`'s file-level AI SUMMARY and
`pengdows.crud.IntegrationTests/ErrorHandling/SerializationConflictTests.cs`) — that investigation
also caught a real self-inflicted test-design bug along the way (a "different tables" test that
accidentally re-tested "same row" twice in parallel and gave a misleading result), which is why
each phase below is specified precisely, including exactly which rows/tables each writer touches.

## What's currently just asserted, not verified

From `AccessDialect.cs`'s header comment (unchanged since the original, now-unrecoverable script):

> while one connection holds an open, uncommitted write transaction, a second connection's INSERT
> of a completely different row into the same table blocks, then fails outright with
> "OleDbException: Could not update; currently locked."

This claims the conflict is **table-scoped** (or page-scoped), not row-scoped — i.e. broader than
DuckDB's confirmed row-scoped conflict. That distinction matters: it's the difference between "safe
as long as two writers don't touch the same row" (DuckDB's actual behavior, confirmed) and "safe
only if two writers never touch the same table at the same time" (Access's claimed, unconfirmed
behavior). Phase 3 below tests exactly this.

## Prerequisites

- A Windows machine.
- [Microsoft Access Database Engine Redistributable](https://www.microsoft.com/en-us/download/details.aspx?id=54920)
  (provides `Microsoft.ACE.OLEDB.16.0` and the `ADOX.Catalog` COM type used to create `.accdb`
  files). `AccessTestContainer.cs` already depends on this being installed — if
  `INCLUDE_ACCESS=true dotnet run -f net10.0 --project testbed -- --only "Access"` already works on the
  machine, this is satisfied.
- .NET 8 SDK.
- A checkout of this repo (`pengdows.crud`) so the probe can reference the real `pengdows.crud`
  project and exercise `DatabaseContext`/`DbMode`, not just raw `OleDb`.

## Test matrix

Each phase specifies exactly which rows/tables each writer touches — do not "simplify" this when
implementing; the DuckDB investigation showed how easy it is to accidentally retest a different
scenario than intended.

| # | Scope | Rows touched | Bypasses pengdows.crud? | Hypothesis | What it would mean if wrong |
|---|---|---|---|---|---|
| 1 | Re-confirm original claim | 20 writers, disjoint rows, **same table** | Yes (raw `OleDbConnection`) | Fails — reproduces `OleDbException: Could not update; currently locked.` | If it now succeeds, the header comment's core claim needs retracting, not just re-wording |
| 2 | Classic write-write conflict | 20 writers, **same row**, same table | Yes (raw `OleDbConnection`) | Fails, same exception | Establishes the row-level case fails too (expected — a superset of #1's claim) |
| 3 | **The open question** | 20 writers, disjoint rows, **two different tables** (10 writers/table, each writer's row unique — no writer anywhere touches another writer's row) | Yes (raw `OleDbConnection`) | Unknown — this is what's actually unverified | If it succeeds, Access's lock granularity is narrower than the header comment implies (table- or page-scoped only, not file-scoped) and `DescribeStandardModeRisk()`'s wording should be corrected to match DuckDB's more precise "same-row/same-resource" framing |
| 4 | The fix | 20 writers, same shape as #1, through `DatabaseContext` with `DbMode.SingleWriter` | No | Zero exceptions, all 20 rows land | If it fails, the governor itself has a bug — treat as a real regression, not a docs update |
| 5 | The hazard, through the framework | 20 writers, same shape as #1, through `DatabaseContext` with `DbMode.Standard` (now honored for Access — see `AccessDialect.CoerceConnectionMode`) | No (goes through pengdows.crud's ephemeral pooled `OleDbConnection`s) | Reproduces #1's failure through the public API, not just raw `OleDb` | If it *doesn't* fail, pengdows.crud's connection pooling/session handling is somehow absorbing the conflict — investigate before trusting the warning text |

Optional, if time allows — Jet/ACE has a `Jet OLEDB:Locking Mode` connection-string/registry
property (`Jet OLEDB:Database Locking Mode=1` for row-level, the modern default; `0` requests the
legacy page-level scheme prior to Access 2000/Jet 4). If phase 3 comes back "fails," it's worth
re-running with each locking mode explicitly set, since the header comment's "page-level/`.laccdb`"
language suggests a page-level model was assumed, not necessarily confirmed. This isn't required to
answer the main question, just useful context if the result is surprising.

## Reference implementation

Adapt the structure below (mirrors the DuckDB probe used this session, but does not exist as a file
in this repo — Access can't run here). Create it as a standalone console project on the Windows
machine (not part of this repo's build) so it doesn't need cross-platform guards:

```xml
<!-- access-concurrency-probe.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="C:\path\to\pengdows.crud\pengdows.crud\pengdows.crud.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// Program.cs — Windows only (System.Data.OleDb + ADOX).
using System.Collections.Concurrent;
using System.Data;
using System.Data.OleDb;
using System.Runtime.InteropServices;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;

const int writerCount = 20;

// --- helper: create a fresh .accdb via ADOX (same mechanism as AccessTestContainer.cs) ---
string CreateAccdb()
{
    var path = Path.Combine(Path.GetTempPath(), $"access-probe-{Guid.NewGuid():N}.accdb");
    var catalogType = Type.GetTypeFromProgID("ADOX.Catalog")
        ?? throw new InvalidOperationException("ADOX.Catalog not found — install the Access Database Engine Redistributable.");
    dynamic catalog = Activator.CreateInstance(catalogType)!;
    catalog.Create($"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={path};");
    Marshal.ReleaseComObject(catalog);
    return path;
}

string ConnStr(string path) => $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={path};";

void RawConcurrentWrite(string label, bool disjointRows, string[] tables)
{
    var path = CreateAccdb();
    Console.WriteLine($"{label}, file={path}");

    using (var setup = new OleDbConnection(ConnStr(path)))
    {
        setup.Open();
        foreach (var t in tables)
        {
            using var cmd = setup.CreateCommand();
            cmd.CommandText = $"CREATE TABLE {t} (id COUNTER PRIMARY KEY, val TEXT)";
            cmd.ExecuteNonQuery();
        }
    }

    var exceptions = new ConcurrentBag<(string Table, int Id, string Message)>();
    var succeeded = new ConcurrentBag<string>();

    var tasks = Enumerable.Range(0, writerCount).Select(i =>
    {
        var table = tables.Length == 1 ? tables[0] : tables[i % tables.Length];
        return Task.Run(() =>
        {
            try
            {
                using var conn = new OleDbConnection(ConnStr(path));
                conn.Open();
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"INSERT INTO {table} (val) VALUES ('row-{i}')";
                Thread.Sleep(200); // widen the overlap window, same as the DuckDB probe
                cmd.ExecuteNonQuery();
                tx.Commit();
                succeeded.Add(table);
            }
            catch (Exception ex)
            {
                exceptions.Add((table, i, $"{ex.GetType().Name}: {ex.Message}"));
            }
        });
    }).ToArray();

    Task.WaitAll(tasks);

    Console.WriteLine($"  Succeeded: {succeeded.Count}/{writerCount}");
    Console.WriteLine($"  Failed:    {exceptions.Count}/{writerCount}");
    foreach (var (table, id, msg) in exceptions)
    {
        Console.WriteLine($"    [{table}:{id}] {msg}");
    }

    try { File.Delete(path); } catch { /* best-effort cleanup */ }
    Console.WriteLine();
}

async Task ThroughPengdowsAsync(DbMode mode, string label)
{
    var path = CreateAccdb();
    Console.WriteLine($"{label}, file={path}");

    var cfg = new DatabaseContextConfiguration
    {
        ConnectionString = ConnStr(path),
        DbMode = mode,
        ReadWriteMode = ReadWriteMode.ReadWrite
    };
    await using var context = new DatabaseContext(cfg, OleDbFactory.Instance);
    Console.WriteLine($"  Resolved ConnectionMode: {context.ConnectionMode}");

    await using (var sc = context.CreateSqlContainer())
    {
        sc.Query.Append("CREATE TABLE probe (id COUNTER PRIMARY KEY, val TEXT)");
        await sc.ExecuteNonQueryAsync();
    }

    var exceptions = new ConcurrentBag<(int Id, string Message)>();
    var succeeded = new ConcurrentBag<int>();

    var tasks = Enumerable.Range(0, writerCount).Select(i => Task.Run(async () =>
    {
        try
        {
            await using var sc = context.CreateSqlContainer();
            sc.Query.Append($"INSERT INTO probe (val) VALUES ('row-{i}')");
            await sc.ExecuteNonQueryAsync();
            succeeded.Add(i);
        }
        catch (Exception ex)
        {
            exceptions.Add((i, $"{ex.GetType().Name}: {ex.Message}"));
        }
    })).ToArray();

    await Task.WhenAll(tasks);

    Console.WriteLine($"  Succeeded: {succeeded.Count}/{writerCount}");
    Console.WriteLine($"  Failed:    {exceptions.Count}/{writerCount}");
    foreach (var (id, msg) in exceptions.OrderBy(e => e.Id))
    {
        Console.WriteLine($"    [{id}] {msg}");
    }

    try { File.Delete(path); } catch { /* best-effort cleanup */ }
    Console.WriteLine();
}

Console.WriteLine("=== Access concurrent-writer verification ===\n");

// Phase 1: re-confirm the original claim (disjoint rows, same table).
RawConcurrentWrite("Phase 1: raw OleDb, DISJOINT rows, SAME table", disjointRows: true, tables: new[] { "probe" });

// Phase 2: classic same-row conflict. Note: Access's COUNTER (autoincrement) PK makes forcing
// a genuine same-ROW UPDATE conflict require a pre-seeded row and explicit UPDATE, not INSERT —
// adjust this phase to seed one row and have all 20 writers UPDATE it, mirroring the DuckDB
// probe's Phase 1b/4/5 exactly, rather than reusing the INSERT shape above.

// Phase 3: THE OPEN QUESTION — disjoint rows, two different tables.
RawConcurrentWrite("Phase 3: raw OleDb, DISJOINT rows, TWO tables", disjointRows: true, tables: new[] { "probe_a", "probe_b" });

// Phase 4: the fix, through pengdows.crud.
await ThroughPengdowsAsync(DbMode.SingleWriter, "Phase 4: pengdows.crud, DbMode.SingleWriter (governed)");

// Phase 5: the hazard, through pengdows.crud's own Standard-mode ephemeral connections.
await ThroughPengdowsAsync(DbMode.Standard, "Phase 5: pengdows.crud, DbMode.Standard (explicitly honored, ungoverned)");
```

This is a starting skeleton, not a finished script — in particular, Phase 2 needs the same-row
`UPDATE` shape spelled out explicitly (see the table above and the DuckDB probe's structure for the
pattern: seed one row, have every writer `UPDATE ... WHERE id = <fixed>`, then verify the final
counter value). Run each phase 2-3 times, the same way the DuckDB investigation did — a single run
isn't enough to trust either a "succeeded" or "failed" result, since scheduling-dependent
overlap can make either outcome look artificially consistent by chance.

## After running it: what to update

Depending on what Phase 3 shows:

- **If it fails** (conflict reproduces across different tables too): the current `DescribeStandardModeRisk()` wording in `AccessDialect.cs` is directionally correct (broad, not narrowly row-scoped) — but strengthen it into "CONFIRMED LIVE (re-verified <date>)" instead of resting on the original, uncommitted script, and add the actual live numbers (N/20 failed) the way `DuckDbDialect.cs` now does.
- **If it succeeds** (different tables are safe): `DescribeStandardModeRisk()` and `AccessDialect.cs`'s header comment need correcting to the narrower, more precise "same table" (or "same page", if the optional locking-mode variant narrows it further) framing — matching how DuckDB's "same-row" framing was arrived at only after disambiguating a misleading intermediate result.

Either way:

1. Update `AccessDialect.cs`'s file-level AI SUMMARY and `DescribeStandardModeRisk()` with the real, dated findings (see `DuckDbDialect.cs` for the pattern to follow).
2. Update `CLAUDE.md`'s "DuckDB and Access similarly allow..." paragraph and `docs/connection/connection-modes.md`'s "Standard → honored, not coerced" entry to match.
3. If feasible, add a Windows-gated (`[SkippableFact]`/`OperatingSystem.IsWindows()`) permanent regression test analogous to
   `SerializationConflictTests.DuckDb_ConcurrentSameRowUpdates_UnderSingleWriterMode_AllSucceedWithZeroConflicts`
   — even though it won't run in this repo's (Linux) CI, it gives future Windows-based verification
   a committed, re-runnable artifact instead of another ad hoc script that disappears.
