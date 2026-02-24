## REVIEW_POLICY.md (canonical)

Use this as the single source of truth for Claude/Codex/Gemini/Copilot reviews.

---

# Code Review Policy — pengdows.crud

## 1) Scope rules

* Review **only the PR diff** plus any code directly referenced by the diff.
* Do not propose large refactors unless a **P0 blocker** requires it.
* Prefer smallest changes that preserve existing public contracts.

---

## 2) Review output contract

Every review must output, in this order:

1. **MERGE:** YES/NO
2. **Blockers (P0)** — must fix
3. **Majors (P1)** — should fix before merge unless explicitly waived
4. **Minors (P2)** — optional/nits
5. **Missing evidence** — tests/profiling/threat note required
6. **DB impact notes** — only if SQL/dialect/DDL/transaction behavior changed
7. **Minimal patch guidance** — concrete steps, no handwaving

---

## 3) P0 blockers (no merge)

### A. Tests (TDD enforcement)

* Any new behavior or bug fix **must include unit tests**.
* Any change affecting DB behavior, SQL generation, transactions, pooling, or dialects **must include integration coverage** (testbed / IntegrationTests).
* No skipped tests.

**Block if** behavior changed but tests did not change.

### B. Security invariants

Block if any of these occur:

* Unvalidated input crosses a trust boundary (SQL, file paths, serialization, network calls, logging).
* Authentication/authorization logic changes without explicit tests.
* Secrets or sensitive values can appear in logs or exception messages.
* Custom crypto/token schemes introduced or modified without clear invariants + tests.

### C. Performance evidence (Abrash rule)

* If PR claims perf improvement, includes optimization, or touches known hot paths: require evidence.

  * Acceptable evidence: BenchmarkDotNet output, profiler summary, or before/after timing numbers.
* Block “optimization by belief”.

### D. API contract safety (interface-first)

* Public API surface must remain stable unless explicitly intended.
* All public APIs live in `pengdows.crud.abstractions`.
* Changes to interfaces require baseline verification updates (or should fail).

### E. Project hard bans

* **TransactionScope is forbidden** (must use `BeginTransaction`).
* No string interpolation for SQL values.
* No unquoted identifiers in custom SQL: use `WrapObjectName`.

---

## 4) P1 majors (fix or justify)

### A. pengdows.crud core invariants

* **ValueTask in hot paths**: execution methods return `ValueTask`/`ValueTask<T>` (do not regress to `Task`).
* **No public constructors** on implementation types except `DatabaseContext`.
* **Interface-first**: consumers depend on abstractions, not implementation types.
* **TableGateway extension rule**: extend TableGateway; do not wrap it with service layers.
* **[Id] vs [PrimaryKey]**: never apply both to the same property; preserve documented upsert key priority.

### B. Deterministic resource ownership

* Dispose readers promptly (`ITrackedReader` is a lease).
* No async leaks, no undisposed containers/readers/transactions.
* No long-lived transactions stored as fields.

### C. SQL correctness and multi-dialect awareness

* Changes to dialects/SQL generation must consider:

  * quoting rules
  * parameter marker rules
  * upsert behavior per DB
  * transaction/isolation semantics
* If a change risks a specific DB family, call it out and require a targeted integration test.

---

## 5) P2 minors (nice to fix)

* Naming, local clarity, consistent style with the repo.
* Minor allocations or micro-optimizations in non-hot paths.
* Small simplifications that do not alter behavior.

---

## 6) Evidence requirements (“prove it”)

A reviewer must request the following when applicable:

### Tests

* Unit tests for behavior changes.
* Integration tests for DB-facing changes.
* Regression tests for bug fixes.

### Performance

Required when:

* PR claims perf improvement
* PR changes pooling, container execution, mapping, or dialect hot paths

### Threat note (security changes)

Required when touching:

* authn/authz
* token handling
* SQL building
* deserialization
* filesystem paths
* logging

Threat note format (5 bullets):

* entry points
* trust boundaries
* assets
* attacker goals
* mitigations + tests

---

## 7) Waivers

* Waiving a **P1 major** requires:

  * explicit written justification in PR description
  * a follow-up issue link
* **P0 blockers cannot be waived.**

---

## 8) DB impact notes

If SQL/dialect behavior changes, reviewers must state:

* affected DBs (at least by family: Postgres-like, MySQL-like, SQL Server-like, embedded, warehouse)
* expected behavior differences
* which integration tests cover it (or what new test is required)

---

## 9) Reviewer personas (how to think)

* **Security:** assume hostile inputs, insist on invariants + tests.
* **Performance:** measure first, optimize last.
* **Design/Testability:** if it’s hard to test, design is wrong.
* **Clarity:** code should read like an executable spec.

---

### Notes (repo-specific)

This policy aligns with existing repo mandates:

* TDD required, integration suite required, ValueTask hot paths, TransactionScope ban, interface-first, and multi-dialect correctness constraints.   

