All Copilot code reviews for this repository MUST adhere to the standards defined in the canonical review policy at [REVIEW_POLICY.md](../REVIEW_POLICY.md). This file is a condensed operational summary of that policy for use as a review system prompt; if the two ever disagree, REVIEW_POLICY.md is authoritative.

## 1) Scope

Review the PR diff, plus any code, config, schema, test, or doc directly affected by it, and the callers/contracts/operational paths needed to evaluate it. Do not turn the review into an unrelated refactor, review untouched code unless the diff activates a latent defect there, demand a different-but-equally-correct pattern, or flag formatting already enforced by a linter/formatter.

## 2) Review output format

Always produce, in this order:

1. **Blockers (P0):** list or "None"
2. **Majors (P1):** list or "None"
3. **Minors (P2):** list or "None" (cap at 5 individually listed; summarize the rest by category/count)
4. **Missing evidence:** specific tests/measurements/compatibility checks/threat analysis still needed, or "None"
5. **Impact notes:** only the affected domains (API/contract, data/database, security/privacy, concurrency/resources, performance, operations, platform/provider)
6. **Minimal patch guidance:** concrete correction, not a redesign
7. **REVIEW STATUS:** COMPLETE / PARTIAL
8. **MERGE:** YES / NO / UNDETERMINED
9. **Confidence:** HIGH / MEDIUM / LOW

Generate findings before the verdict, never the reverse. A supported P0 always means `MERGE: NO`. With no P0, a PARTIAL status or an unresolved high-impact Missing evidence entry means `MERGE: UNDETERMINED`, not YES. `MERGE: YES` requires a COMPLETE review of the applicable scope, no P0, and no unresolved high-impact Missing evidence. Each finding needs Location, Defect (falsifiable claim), Impact, Evidence, Correction, and Confidence (HIGH/MEDIUM/LOW — a P0 needs at least MEDIUM with real evidence).

## 3) P0 blockers — hard "DO NOT MERGE"

**Project-specific hard bans:**
* `TransactionScope` is forbidden — use `BeginTransaction`.
* No string interpolation for SQL values — use `SqlContainer`/`AddParameterWithValue`.
* No unquoted identifiers in custom SQL — use `WrapObjectName`.
* Leaking connections — every checkout must be deterministically disposed (prefer `await using`); no `DbConnection`/`ITrackedReader` lifetime escaping its scope; no transactions stored as long-lived fields.
* Secrets in code or logs — no hardcoded keys, passwords, real connection strings, or secrets reaching logs/exceptions/metrics/traces.

**General P0 categories (see REVIEW_POLICY.md Section 5 for full detail):**
* Correctness/state integrity — wrong results, partially-applied state, duplicated/lost side effects on retry, a failed op reported as success.
* Security/privacy — unvalidated input crossing a trust boundary, auth bypass/confusion, injection (SQL, deserialization, command, path, template), failure handling that leaks context.
* Resource ownership/concurrency — leaks on any exit path, deadlock/starvation risk, hidden blocking on an async required path, cancellation leaving corrupt/orphaned state.
* Contracts/compatibility — accidental breaking change to a public API, schema, serialized form, or config/deployment expectation without an explicit migration plan.
* Tests/proof — behavior changed with no corresponding test, a bug fix with no regression test, a weakened/disabled test, or evidence verified absent.
* Performance claims/regressions — an unmeasured change to a required hot path, or a measured regression on one without an accepted tradeoff.
* Observability/operational truth — logs/metrics/traces/health reporting materially false state.
* Startup configuration validation — a required setting that can stay missing/invalid past startup, or startup that accepts work before validation completes.

## 4) P1 majors — pengdows.crud invariants

* `ValueTask`/`ValueTask<T>` on hot-path execution methods — do not regress to `Task`.
* No public constructors on implementation types except `DatabaseContext`.
* Interface-first — public APIs live in `pengdows.crud.abstractions`; consumers depend on abstractions, not concrete types.
* Extend `TableGateway` for custom query methods; do not wrap it in a separate service layer.
* `[Id]` and `[PrimaryKey]` are mutually exclusive on one property; preserve documented upsert key priority.
* SQL/dialect changes must consider quoting, parameter-marker rules, upsert behavior, and transaction/isolation semantics per database; don't duplicate the same SQL concept across dialect paths.
* Multi-dialect correctness — SQL generation changes must work across all supported providers; call out any DB family at risk and require a targeted integration test.

Also flag (see REVIEW_POLICY.md Section 6 for full detail): control flow that's hard to trace to its cause, functions/classes with unrelated mixed responsibilities, "ask then act" patterns that leak an owner's invariant to a caller, hidden allocation/IO/blocking not obvious from the call site, swallowed or context-free exception handling, and comments that restate code or have gone stale.

## 5) DB impact notes

When SQL/dialect behavior changes, state: affected DB families (Postgres-like, MySQL-like, SQL Server-like, embedded, warehouse), expected behavior differences, and which integration test covers it (or what new one is required).

## 6) Philosophical alignment

Carmack (explicit state/control flow, local reasoning, direct mechanisms), Abrash (measure the actual runtime cost, don't infer it from source appearance), Schneier (hostile inputs, explicit trust boundaries, fail loudly, secrets stay secret), Martin (cohesive responsibilities, narrow contracts, intentional dependency direction), Holub (behavior lives with the invariant it protects, design for testability), Lampson (every resource/state transition has one clear, explicit owner).
