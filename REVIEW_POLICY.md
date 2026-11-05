# REVIEW_POLICY.md (canonical)

The single review policy for human and automated reviews in this repository. It is the
five-ring policy used across `/home/alaricd/prj`, with this repository's rules added as a
project overlay (§9). Every finding maps to one ring. The overlay may add rules; it may never
weaken a P0 in the base.

| Ring | Question it answers | Lens (author) |
|------|--------------------|---------------|
| 1. Correctness and contract | Does it do what it promises, for every input? | **Holub** |
| 2. Boundaries and ownership | Is every trust, resource, thread and I/O boundary explicit and owned? | **Schneier** |
| 3. Cost and performance | What does the machine actually do, and is that cost visible? | **Abrash** |
| 4. Evidence and coverage | Would we know if it were broken? Was that shown, not assumed? | **Carmack** |
| 5. Documentation and maintainability | Can the next reader change it safely, and is the contract written down? | **Martin** |

The lens decides how a finding is judged. It is not a quotation, and nothing here claims an author
said a particular line.

---

## 1) Scope

* Review the diff, plus the code, configuration, schemas, tests and documentation the diff
  touches or depends on, plus the callers and implementations needed to judge a changed contract.
* Do not review untouched code unless the change activates or depends on an existing defect.
* Do not propose refactors, new layers, wrappers or patterns unless a P0 requires them.
* Ignore formatter- or linter-enforced formatting. Ignore generated code unless its generator,
  its contract or its runtime behaviour changed.
* Prefer the smallest change that fixes the problem, keeps valid contracts, and leaves the
  system easier to reason about.

---

## 2) Output contract

Generate findings before the verdict. The verdict must not be the first conclusion produced.
Output in this order:

1. **Blockers (P0)**: defects that prohibit merge. Each cites its ring.
2. **Majors (P1)**: material defects to fix before merge.
3. **Minors (P2)**: optional improvements and local clarity issues.
4. **Missing evidence**: tests, measurements, compatibility checks or threat analysis still required.
5. **Impact notes**: only for affected technical or operational domains (for example database
   families, concurrency, or public API surface).
6. **Minimal patch guidance**: concrete correction, not a redesign.
7. **REVIEW STATUS**: `COMPLETE` or `PARTIAL`.
   * `COMPLETE` means everything §1 places in scope was accessed and evaluated.
   * `PARTIAL` means something in scope was inaccessible, omitted or sampled. Name it.
8. **MERGE**: `YES` or `NO`.
   * `NO` if any P0 is open.
   * `NO` if a required check could not run, or the status is `PARTIAL`. An undetermined result is never a merge.
   * `YES` only when there are no open P0s, all required evidence is present, and the status is `COMPLETE`.
9. **Confidence**: `HIGH` (directly evidenced by the diff or by a run), `MEDIUM` (strongly
   indicated, needs confirmation) or `LOW` (plausible, limited evidence).

Every finding has: file and line, ring, severity, one-sentence statement of the defect, a
concrete failure scenario (input or state, then the wrong output or crash), and confidence.

---

## Ring 1: Correctness and contract (lens: Holub)

Holub's lens: behaviour belongs with the structure that owns the data it manipulates, and
objects should expose capabilities rather than data. Correctness is judged against the contract
that callers rely on.

### P0 (blocks merge)

* A behaviour change or bug fix has no test that fails before and passes after (see Ring 4).
* A public or documented contract changes without that intent being stated and versioned.
* An error path leaves state inconsistent (partial writes, half-applied batches, dangling locks).
* A retry can duplicate a side effect.
* The change gives wrong results for valid, boundary or adversarial input.
* Business rules are placed in an I/O adapter in a way that bypasses or duplicates domain rules.
* Metrics no longer tell the truth after a behaviour change. A change that silently breaks a metric is
  treated the same as one that breaks a query.

### P1 (fix before merge)

* **Behaviour locality**: logic sits away from the data it governs. Flag "ask then act": code
  that reads state only to decide something elsewhere.
* **Capabilities over data**: a getter exists so outside code can enforce an invariant that
  belongs to the owning type. Do not flag plain DTOs, value objects or read models.
* **Adapter purity**: controllers, endpoints and route handlers only bind input, resolve
  auth or tenant context, delegate, and format output. Persistence adapters only map, build
  queries, execute and manage transactions. Business rules and multi-entity orchestration
  live in domain code that runs without I/O.
* **Service wrappers** that only forward calls to a data-access object without adding a rule.
* **Inheritance** where composition would serve. Inheritance is coupling.
* **Interface-first**: consumers depend on abstractions, not concrete types, across module boundaries.
* **Move errors left**: a compile-time guarantee turned into a runtime check is flagged.
* **Exception behaviour** changed without a test that pins the new behaviour.

### P2

* Naming that obscures what a result means. Dead branches that do not change behaviour.

---

## Ring 2: Boundaries and ownership (lens: Schneier)

Schneier's lens: assume hostile input at every trust boundary, fail loudly, and keep secrets
out of places they can leak. Lampson's ownership rule is folded in here: every resource has
exactly one owner, and it is clear who releases it.

Boundaries in scope: network and connection pools, database connections and readers, transactions,
thread pools and async continuations, memory and buffer ownership, authentication and tenant
context, serialization, file paths, and logs and telemetry.

### P0 (blocks merge)

* Unvalidated input crosses a trust boundary: SQL, file paths, deserialization, network calls,
  logging, connection strings.
* Authentication or authorisation logic changes without explicit tests.
* A secret or sensitive value can reach a log, exception message, metric or telemetry field.
* Custom cryptography or token handling is introduced or modified without stated invariants and tests.
* A failure is silent: swallowed exceptions, ignored return codes, or a catch with no logging
  and no stated reason. Silence is a security defect, not a style issue.
* A boundary becomes implicit or uncontrolled (a connection, pool, thread or auth context
  that nothing scopes or releases).
* A resource's lifetime escapes the scope that controls it, or two components can both
  believe they own it. Ask: who owns this? who releases it? can ownership escape its scope?
* Async or error paths allow a leak of connections, readers, transactions, streams or handles.
* Raw ADO.NET objects (`DbConnection`, `DbCommand`, `DbDataReader`, `DbTransaction`, `DbDataSource`
  and their `System.Data` interfaces) are returned, exposed as a property, field or event, or reachable
  from public API, or a public type inherits one. Pool governance depends on callers never holding one.
  The sanctioned form is a governed wrapper that holds the lease and releases it on disposal. A raw
  object accepted as an input needs a recorded reason.
* A change to a shipped raw-object exposure, or a new one, is not recorded in the public surface guard
  (`PublicSurfaceGuardTests`, see §11).

### P1

* An empty or comment-only catch with no stated reason. Narrow it, log it, or rethrow it. An empty
  catch must say, in a comment, which exception is expected and why swallowing it is correct.
* A resource is not disposed deterministically (`using`, `await using`, or equivalent).
* Ownership is handed off without a comment or type that makes the handoff visible.
* Deserialization or parsing of untrusted data without size or depth limits.

### Threat note (required for changes touching authn/authz, tokens, query building,
deserialization, file paths, logging or connection strings)

* Entry points
* Trust boundaries
* Assets at risk
* Attacker goals
* Mitigations and the tests that prove them

---

## Ring 3: Cost and performance (lens: Abrash)

Abrash's lens: know what the machine is actually doing. If something looks simple and cheap,
verify it. Optimisation by belief is forbidden, and hidden costs are design defects.

### P0 (blocks merge)

* A performance claim, or an optimisation, with no measurement.
* A change to a known hot path that could affect runtime cost, with no measurement. Hot paths include
  per-command execution, per-row mapping and coercion, and the per-event metrics path.
* Blocking work (synchronous I/O, `.Result`, `.Wait()`, lock waits) inside an async path on a hot path.
* A hidden cost on a known hot path: per-call allocation, implicit I/O, hidden query generation.

### P1

* A method that does hidden work not visible at the call site: hidden allocations, implicit I/O,
  implicit query or SQL generation, blocking in async code off the hot path, expensive LINQ or
  enumeration pipelines.
* Hot-path methods that regress from a cheaper return or dispatch shape to a costlier one, without a measurement.
* Unbounded growth: caches, buffers, queues or retries with no ceiling.

### P2

* Micro-allocations or micro-optimisations in non-hot code, where they do not change behaviour.

### Measurement rules

* Use the project's benchmark harness (for example BenchmarkDotNet). Acceptable evidence is
  harness output, a profiler summary, or before-and-after timings from the same machine.
* Use correct warm-up. Report cold-path and steady-state separately where both matter.
* A microbenchmark must isolate the code under test. Benchmarking a surrounding pipeline is not evidence.
* The measured delta must clearly exceed the noise floor. Overlapping confidence intervals are not evidence.
* Report the machine and the runtime version alongside the numbers.

---

## Ring 4: Evidence and coverage (lens: Carmack)

Carmack's lens: get the real thing running, measure it, iterate on tight feedback, and test the
hostile cases, not only the happy path. A check that was not run is not a pass.

### P0 (blocks merge)

* Behaviour changed and no test changed.
* A bug fix with no regression test that fails without the fix.
* A test was skipped, or an early `return` stands in for an assertion, and the skip is not
  justified by a documented capability limit. Capability skips only: a test may skip solely because the
  database lacks the feature under test, and each such skip is attributed `[SKIP:<Product>]`. Any other skip,
  and any silent early `return` standing in for one, is a blocker.
* A DB-facing, SQL-generating, transaction or pooling change with no integration coverage
  against a real engine. Any change affecting DB behaviour, SQL generation, transactions, pooling, or dialects
  must include integration coverage.
* A concurrency-sensitive change (upsert, merge, idempotent write, shared cache) with no
  concurrent test. A single-threaded test cannot show a race. Any change to an upsert, merge or other
  idempotent write needs a concurrent same-key test per affected database family, run against the real database.
* Any required check failing, unrun or blocked, reported as passing.

### P1

* Boundary and adversarial inputs missing for string handling, identifier generation, query
  building, parsing or type mapping. Test with values longer, stranger and more hostile than any
  example in the implementation: very long strings, nulls, reserved words, embedded parameter
  markers, Unicode edge cases, and values at the limit of the underlying type.
* Tests that assert internal calls or internal structure instead of observable outcomes.
* Coverage that falls below the project minimum, or patch coverage that is not measured.
* Failure paths not exercised: the error branch exists but no test drives it.

### P2

* Test names that do not state intent. `Test1` is not a name.

### Evidence rules

* Unit tests for behaviour changes. Integration tests for persistence-facing changes.
  Regression tests for bug fixes.
* Report what was measured (coverage, benchmark, test counts) and what could not be measured.
* Name each unavailable check explicitly and keep the work marked unverified until it runs.
* Never report an unavailable environment as a pass.

---

## Ring 5: Documentation and maintainability (lens: Martin)

Martin's lens: small, cohesive units with clear names, a simple design that earns its
complexity, and code that reads as its own specification. Documentation states the contract
the code cannot express.

### P1

* A function does more than one conceptual thing, or its main flow is obscured by a sub-operation
  that should have a name.
* More than three parameters without a parameter object or richer type.
* Many constructor dependencies, which usually means the class is doing too much.
* Complexity without a stated reason. Solve the problem that exists, not the one imagined.
* A public API change with no release note, no declared intent, and no migration note.
* A comment that restates the code, or a ceremonial header (authorship or history belongs in source control).
* A comment that hides a confusing name or structure that should be fixed instead.
* Inheritance where composition would serve.

### P2

* Naming, local clarity, consistency with the surrounding code.

### What a comment is allowed to do

A comment is justified only to explain what the code structurally cannot express: a
workaround for a known bug, a regulatory constraint, or a non-obvious invariant. Hot-path
or invariant context headers are allowed where the diff alone cannot establish them.

### Documentation references

* Changes to behaviour that is specified elsewhere cite the specification: standard, RFC,
  vendor documentation or API contract. Note whether the change restores the contract or
  intentionally differs from it.
* The PR description states: what changed, why, the affected versions, the verification run
  and its results, and the known gaps.

---

## 6) Severity and waivers

* **P0** cannot be waived.
* **P1** may be waived only by showing it is a false positive: the rule does not apply in the
  actual context. Deferral is not a waiver. A deferred concern needs a follow-up issue.
* **P2** is advisory.
* A finding's ring is the ring of its lens, not the severity. A P0 in Ring 4 blocks merge the
  same as a P0 in Ring 2.

---

## 7) Upstream contributions

When the change is a fix for a defect in an upstream library, provider, driver or framework,
the policy above applies, plus `AGENTS.md`'s upstream bug workflow:

* Establish the upstream repository, fork, target branch and current commit before judging the fix.
* Reproduce against the updated upstream baseline with the smallest regression test. Do not label a defect
  upstream without evidence that the local code is not the cause.
* For ADO.NET or provider behaviour, check the applicable `System.Data.Common` contract,
  the API documentation, and at least one mature peer provider. Record the links or clauses.
* For `GetSchema` defects, treat each collection as a complete metadata contract. Populate all
  applicable columns, prefer the database's own catalogs, and test shape, values, nullability and provider-specific metadata.
* Record discovery attribution (for example "found by testing for pengdows.crud", or a direct customer repro).
* The PR must state the affected upstream version or commit and any remaining environment limitation.

---

## 8) Reviewer personas

Each ring is judged by one question. Ask it of every change.

* **Ring 1 (Holub):** Where does the behaviour live, and is it near the data it governs? If it is hard to test, is the design wrong?
* **Ring 2 (Schneier):** Who is hostile here? Where does control cross a boundary, and who owns the resource on each side?
* **Ring 3 (Abrash):** What does this actually do at runtime? What costs are hidden from the call site?
* **Ring 4 (Carmack):** What is the test that would have caught this? Did I run it, or am I assuming it passes?
* **Ring 5 (Martin):** Can the next reader change this without fear? Does the name say what the code does?

---

## 9) Project overlay: pengdows.crud

This overlay is the repository's project rules. It names this file as its base, adds rules, and puts
each rule under the ring it belongs to. It removes or downgrades nothing in the base.

### Ring 1 (correctness and contract)

* **P0, I/O separation.** Business logic in `TableGateway`, `PrimaryKeyTableGateway`, SQL builders or
  repositories is a blocker. Gateways only build SQL, map rows, execute statements, and commit or roll back.
  Domain logic may not depend on `IDbConnection`, `IDbCommand`, `HttpContext`, `ClaimsPrincipal` or `IResult`.
* **P0, metrics integrity.** A change that silently breaks a metric, or makes two instruments disagree on
  what a label means, is a blocker unless the disagreement is documented in `docs/opentelemetry-metrics.md`.
* **P1, TableGateway extension rule.** Extend `TableGateway`; do not wrap it in a service layer.
* **P1, `[Id]` versus `[PrimaryKey]`.** Never apply both to the same property. Preserve the documented upsert
  key priority.
* **P1, constructors.** No public constructors on implementation types except the deliberate entry points:
  `DatabaseContext`, `TableGateway<,>`, `PrimaryKeyTableGateway<>`, and the tenant helpers
  (`TenantConnectionResolver`, `TenantContextRegistry`). A new public constructor needs a recorded reason.
* **P1, public API declarations.** Every new public or protected member is declared in its project's
  `PublicAPI.Unshipped.txt` with a one-line reason. An undeclared addition is a P0 blocker (RS0016). Projects
  without a `PublicAPI` file cannot declare their public surface; that gap is itself a P1 finding.
* **P1, interface-first.** Consumers depend on `pengdows.crud.abstractions`. Public APIs live there.
* **P1, SQL dialect awareness.** A change to dialects or SQL generation considers quoting rules, parameter
  marker rules, upsert behaviour per database, and transaction and isolation semantics. Duplicate dialect code
  paths representing the same SQL concept are a P1.
* **P1, ValueTask in hot paths.** Execution methods return `ValueTask` or `ValueTask<T>`. Do not regress to `Task`.

### Ring 2 (boundaries and ownership)

* **P0, transactions.** `TransactionScope` is forbidden. Use `BeginTransaction`. Transactions are never stored in
  long-lived fields.
* **P0, SQL injection.** No string interpolation or concatenation for SQL values. Values are parameterised with
  `AddParameterWithValue` or `CreateDbParameter`. `pengdows.crud.analyzers` enforces this as `PGC008`.
* **P0, identifiers.** No unquoted identifiers in custom SQL. Use `WrapObjectName`, never hard-coded quoting.
* **P0, mocking.** Never mock ADO.NET provider behaviour. Connections, commands, readers, transactions,
  parameters, factories, data sources and their failures run on `pengdows.crud.fakeDb`, which must produce anything
  a real provider can. A gap is added to fakeDb, never worked around with a mock. Mocking other things (the
  library's own interfaces, loggers, resolvers) is fine and may use Moq. `NoMockedProviderTypesTests` fails the
  build when a mocking framework or `DispatchProxy` creates a provider type.
* **P0, empty catches.** An empty or comment-only catch must state, in a comment, the exception it expects and
  why swallowing it is correct. Otherwise narrow it, log it, or rethrow it (CATCH-001 in `docs/FUTURE_WORK.md`).
  Enforced by `Catch001_EveryCommentOnlyCatchStatesAReason` and `Policy4B_NoOneLineEmptyCatch`.
* **P0, raw ADO.NET exposure.** The raw-object rule in Ring 2's base section applies in full. Today the only
  sanctioned governed wrapper is `ITrackedReader`. The three shipped `DataSource` properties on
  `DatabaseContext` and `TransactionContext` are a documented, grandfathered exception (2.0.5), removable only in 3.0.
  Any new exposure is a P0.

### Ring 5 (maintainability and style)

These are project-wide style invariants. They are P0 because the same kind of defect hides in a diff when they
are broken.

* **P0, one statement per line.** `if (something) break;` on one line is forbidden. Breakpoints become ambiguous.
* **P0, braces on all control flow.** Brace-free single-line blocks are forbidden. Enforced by the build
  (`WarningsAsErrors`, `EnforceCodeStyleInBuild`) and by `LibrarySourceAuditTests.Policy4F_NoUnbracedControlFlow`
  and `Policy4F_TheBuildRequiresBraces`.
* **P0, no `else` after a scope exit.** After `return`, `break`, `continue` or `throw`, do not use `else`.
* **P0, explicit parentheses.** Mixed boolean expressions are parenthesised: `(a && b) || c`, never `a && b || c`.
* **P1, comments.** Code is understandable without comments. Decision tags, ticket numbers and dates belong in the
  commit or the decision record, not in source comments.

### Ring 4 (evidence and coverage)

* **P0, test vocabulary.** "Unit tests" never include integration tests. An integration failure is never reported
  as a caveat on a unit run. The solution-level `dotnet test` includes the integration project and is not used for a
  unit run.
* **P0, full pass.** All unit tests and all integration tests must pass. No test may be skipped without a documented
  capability limit. CI enforces a minimum of 83% coverage; target 95% for new work.
* **P0, TDD.** TDD is mandatory for every behaviour change, bug fix, regression fix and public-contract change.
  The failing test is written first and confirmed red for the intended reason before implementation.
  `ReviewPolicyAmendmentTests` checks this document's own requirements.
* **P1, test names.** Name files `*Tests.cs` and mirror source namespaces.
* **P1, source of truth for test vocabulary.** A new database needs a complete integration suite before it is
  supported.

### Ring 3 (cost and performance)

* **P0, measured performance.** A performance claim, an optimisation, or a change to a hot path (per-command,
  per-row mapping and coercion, the per-event metrics path, pooling) requires the measurement rules in Ring 3.
* **P1, UUIDv7.** Generation is non-blocking on counter exhaustion. A change that reintroduces a wall-clock wait
  is a Ring 1 contract change.

---

## 10) Mapping from the previous policy

This policy replaces the previous section numbering. Code comments, tests, `CLAUDE.md`, `AGENTS.md`,
`GEMINI.md` and `docs/FUTURE_WORK.md` cite the old numbers. Use this table to find the new location.

| Previous section | Now |
|---|---|
| §1 Scope | §1 |
| §2 Output contract | §2 |
| §3 Foundational principles | Lenses in the ring table, and each ring's lens |
| §4A Tests (TDD enforcement) | Ring 4 P0 and the overlay's Ring 4 section |
| §4B Security invariants | Ring 2 P0 and the overlay's Ring 2 section |
| §4C Performance evidence | Ring 3 P0 and measurement rules |
| §4D API contract safety | Ring 2 P0 (raw ADO.NET), Ring 1 P1 (public API declarations, constructors) and the overlay |
| §4E Resource lifetime violations | Ring 2 P0 |
| §4F Project hard bans | Overlay Ring 2 and Ring 5 sections |
| §4G Metrics integrity | Ring 1 P0 and the overlay's Ring 1 section |
| §4H Correctness regressions | Ring 1 P0 |
| §4I Separation of business logic from I/O | Ring 1 P1 (adapter purity) and the overlay's P0 |
| §5A pengdows.crud core invariants | Overlay Ring 1 and Ring 3 |
| §5B Function and method design | Ring 5 P1 |
| §5C Behaviour locality | Ring 1 P1 |
| §5D Hidden cost violations | Ring 3 P1 |
| §5E Deterministic resource ownership | Ring 2 P0 and P1 |
| §5F SQL correctness and multi-dialect awareness | Overlay Ring 1 P1 |
| §5G ADO.NET `GetSchema` completeness | §7 Upstream contributions |
| §5H Move errors left | Ring 1 P1 |
| §5I Comments | Ring 5 P1 and the comment rule under Ring 5 |
| §6 P2 minors | §5 P2 items in each ring |
| §7 Evidence requirements | Ring 3 measurement rules and Ring 4 evidence rules |
| §8 Waivers | §6 |
| §9 DB impact notes | §2 impact notes |
| §10 Reviewer personas | §8 |
| §11 Enforcement mechanisms | Overlay, under each rule (the machine-enforced tests and analyzers are named there) |

---

## 11) Enforcement mechanisms

Compliance is enforced in four places. Each entry names the check that enforces it.

1. **Source hygiene tests (`pengdows.crud.Tests/hygiene`)**: `LibrarySourceAuditTests.Policy4F_NoUnbracedControlFlow`
   fails on a one-line `if (x) stmt;` or an unbraced `else`. `Policy4F_TheBuildRequiresBraces` pins the build
   configuration: `.editorconfig` sets IDE0011 to a warning, `EnforceCodeStyleInBuild` reports it, and
   `WarningsAsErrors` makes it an error. `Catch001_EveryCommentOnlyCatchStatesAReason` and
   `Policy4B_NoOneLineEmptyCatch` fail on an empty catch or one with no stated reason.
2. **Behaviour-level tests**: `NoMockedProviderTypesTests` fails the build when a mocking framework or `DispatchProxy`
   creates a provider type. `NoDatabaseTypeBranchingTests` fails when a `SupportedDatabase` value is named outside
   `dialects/` and the detection service. `ReviewPolicyAmendmentTests` checks that this document still states its
   required rules.
3. **Public-API guards**: PublicApiAnalyzers on `pengdows.crud`, `pengdows.crud.abstractions` and `pengdows.crud.fakeDb`.
   An undeclared public or protected member fails the build (RS0016), and a removed one fails it too (RS0017).
   `PublicAPI.Shipped.txt` is the 2.0.5 surface and `PublicAPI.Unshipped.txt` lists what 2.0.6 added.
   `PublicSurfaceGuardTests` ratchets raw ADO.NET outputs and inheritance. Package validation (ApiCompat) compares
   against the last shipped version on `dotnet pack`, and anything it reports needs a recorded decision.
4. **Analyzers**: `pengdows.crud.analyzers` enforces raw predicate and join value injection (`PGC008`), plus the
   gateway and dialect rules (`PGC001`, `PGC025`–`PGC030`).

Manual peer review is the fifth check. Every PR needs a maintainer who verifies adherence to this policy and the
project invariants.
