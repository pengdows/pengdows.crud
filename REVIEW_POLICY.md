# REVIEW_POLICY.md (canonical)

Use this as the single source of truth for Claude/Codex/Gemini/Copilot reviews.

---

## 1) Scope rules

* Review **only the PR diff** plus any code directly referenced by the diff.
* Do not propose large refactors unless a **P0 blocker** requires it.
* Do not propose new architectural patterns, wrappers, service layers, or abstractions unless required to resolve a P0 blocker.
* Ignore unrelated files, formatting-only changes, and generated code unless the diff changes runtime behavior or public contracts.
* Prefer smallest changes that preserve existing public contracts.

---

## 2) Review output contract

Every review must output, in this order:

1. **MERGE:** YES/NO
2. **Blockers (P0)** — must fix
3. **Majors (P1)** — should fix before merge; waivers only for confirmed false positives
4. **Minors (P2)** — optional/nits
5. **Missing evidence** — tests/profiling/threat note required
6. **DB impact notes** — only if SQL/dialect/DDL/transaction behavior changed
7. **Minimal patch guidance** — concrete steps, no handwaving
8. **Confidence:** HIGH / MEDIUM / LOW
   * **HIGH** — directly evidenced by diff or tests
   * **MEDIUM** — strongly indicated but needs confirmation
   * **LOW** — plausible concern, limited evidence

---

## 3) Foundational principles (inform all review decisions)

These are not checklist items — they are the lens through which every finding is evaluated.

**The Boundary Rule** — Systems fail at boundaries. Every boundary must be explicit and controlled: DB connections, thread pools, network calls, memory ownership, authentication context. A change that makes a boundary implicit or uncontrolled is a defect regardless of whether it manifests immediately.

**The Lampson Rule** — Ownership must be explicit. Every resource must have a single clear owner: connections, readers, transactions, memory, and execution contexts. Shared resources without explicit ownership eventually leak, corrupt state, or deadlock. Ask: who owns this resource? Who releases it? Can ownership escape the scope where it is controlled? Can two components believe they own it? If the answer is unclear, the design is wrong. *(Lampson, Hints for Computer System Design, 1983.)*

**The Abrash Rule** — Know what the machine is actually doing. If it looks simple and cheap, verify that it actually is. Optimization by belief is forbidden. Hidden costs are design defects.

**The Schneier Rule** — Assume hostile inputs at every trust boundary. Fail loudly — silent failure is a security defect. Secrets must never appear in logs, exceptions, metrics, or telemetry.

**The Holub/Martin Rule** — If it is hard to test, the design is wrong. Complexity is the enemy. Favor small, cohesive units whose behavior is easy to reason about and verify.

**The Simplicity Rule** — Prefer the simplest design that solves the problem correctly. Complexity must justify its existence. Solve the problem you have, not the one you imagine.

---

## 4) P0 blockers (no merge)

### A. Tests (TDD enforcement)

* Any new behavior or bug fix **must include unit tests**.
* Any change affecting DB behavior, SQL generation, transactions, pooling, or dialects **must include integration coverage**.
* No skipped tests.
* **Block if** behavior changed but tests did not change.

### B. Security invariants

Block if any of these occur:

* Unvalidated input crosses a trust boundary (SQL, file paths, serialization, network calls, logging, connection strings).
* Authentication/authorization logic changes without explicit tests.
* Secrets or sensitive values can appear in logs, exceptions, metrics, or telemetry.
* Custom crypto/token schemes introduced or modified without clear invariants and tests.
* A failure path is silent — swallowed exceptions, unchecked return codes, or missing error logging are security defects, not merely quality issues.
* A boundary (connection, pool, auth context, thread) becomes implicit or uncontrolled.

### C. Performance evidence (Abrash rule)

* If PR claims perf improvement, includes optimization, or touches known hot paths in a way that could affect runtime cost: require evidence.
  * Acceptable evidence: BenchmarkDotNet output, profiler summary, or before/after timing numbers.
  * Benchmarks must demonstrate correct warmup methodology. Cold-path and steady-state results must be distinguished where relevant.
  * Microbenchmarks must isolate the code under test. Benchmarking a surrounding pipeline is not acceptable evidence.
  * The measured delta must exceed the noise floor — overlapping confidence intervals is not evidence.
* Block "optimization by belief."
* Block "it looks simple and cheap therefore it is" reasoning without verification.

### D. API contract safety (interface-first)

* Public API surface must remain stable unless explicitly intended.
* All public APIs live in `pengdows.crud.abstractions`.
* Concrete types must not cross module or service boundaries where an interface exists.
* Changes to interfaces require baseline verification updates.

### E. Resource lifetime violations

Block if:

* `DbConnection` lifetime escapes execution scope.
* `IDataReader` / `ITrackedReader` is not deterministically disposed.
* Transactions are stored in long-lived fields.
* Async paths allow connection or reader leaks.

Resource leaks are architecture violations, not just bugs. The entire pool governance design exists to prevent this class of failure.

### F. Project hard bans

These rules are not stylistic preferences. They exist because violations create conditions where correctness defects become invisible in diffs, debugging sessions, and code review. They are therefore treated as correctness safeguards and enforced as P0 blockers.

* **TransactionScope is forbidden** — use `BeginTransaction`.
* No string interpolation for SQL values.
* No unquoted identifiers in custom SQL — use `WrapObjectName`.
* **One statement per line, always.** `if (something) break;` on one line is forbidden. The break is invisible in a diff and makes debugger breakpoints ambiguous. This is how correctness defects hide.
* **Braces are required for all control flow blocks without exception.** Brace-free single-line blocks are forbidden regardless of brevity. Braces and parentheses compile to nothing — file length is not a valid reason to omit them. An unbraced block is one added line away from a silent correctness bug.
* **After any scope-exiting statement — `return`, `break`, `continue`, or `throw` — do not use `else`.** The else branch is already implied by the exit. An else after a scope exit is logically redundant and adds indentation that obscures control flow.
* **Parentheses must make expression intent explicit.** Do not rely on operator precedence rules where parentheses would remove ambiguity. `(a && b) || c` is required; `a && b || c` is not acceptable.

### G. Metrics integrity

* If behavior changes, verify that metrics still tell the truth.
* A change that silently breaks a metric is equivalent to a change that breaks a query.

### H. Correctness regressions

Block if a change introduces any of the following:

* Error paths that leave state inconsistent.
* Retries that can duplicate side effects.
* Exception handling that changes observable behavior without tests.
* Logic changes that can produce incorrect results for valid, boundary, or adversarial inputs.

---

## 5) P1 majors (fix or justify with false-positive evidence)

### A. pengdows.crud core invariants

* **ValueTask in hot paths** — execution methods return `ValueTask`/`ValueTask<T>`; do not regress to `Task`.
* **No public constructors** on implementation types except `DatabaseContext`.
* **Interface-first** — consumers depend on abstractions, not implementation types.
* **TableGateway extension rule** — extend TableGateway; do not wrap it with service layers.
* **[Id] vs [PrimaryKey]** — never apply both to the same property; preserve documented upsert key priority.

### B. Function and method design (Holub/Martin)

* A function must do one conceptual thing. If a meaningful sub-operation obscures the main flow, extract it into a named function.
* A function must be small enough to understand without losing the control flow in mental paging.
* Parameter count should normally be fewer than four. When more are required, consider whether a parameter object, richer domain type, or decomposition is the correct resolution.
* Constructor parameter count is a class responsibility signal. Many constructor dependencies usually indicate the class is doing too much or sitting at the wrong level of abstraction.
* Inheritance is a form of coupling. Flag new inheritance hierarchies where composition would serve.

### C. Behavior locality (Holub)

* Behavior must live with the structure that owns the data or invariant it manipulates.
* Flag when business logic is implemented in service layers that simply wrap TableGateway calls.
* Flag when SQL logic is implemented outside the gateway responsible for the table.
* Flag "ask then act" patterns — code that retrieves state only to make decisions on it elsewhere. That decision belongs with the object that owns the state.
* Objects should expose capabilities, not data. Flag getters whose primary purpose is to allow outside code to enforce invariants that should live with the owning type. Do not flag simple data carriers, value objects, DTOs, or read models solely for exposing state.

### D. Hidden cost violations (Abrash rule — extended)

Flag methods that introduce hidden work not obvious from the call site: hidden allocations, implicit IO, hidden SQL generation, hidden blocking in async paths, or expensive LINQ pipelines.

Hidden cost violations are P1 unless they affect a known hot path or introduce blocking behavior in async code — in which case report them under P0 instead.

### E. Deterministic resource ownership

* Dispose readers promptly — `ITrackedReader` is a lease.
* No async leaks, no undisposed containers/readers/transactions.
* No long-lived transactions stored as fields.
* Every error path must be explicitly handled and tested — assume things will go wrong.
* Never silently ignore errors.

### F. SQL correctness and multi-dialect awareness

* Changes to dialects/SQL generation must consider: quoting rules, parameter marker rules, upsert behavior per DB, transaction/isolation semantics.
* If a change risks a specific DB family, call it out and require a targeted integration test.
* Do not represent the same SQL concept in two different ways simultaneously — duplicate dialect code paths are a correctness and maintenance hazard.

### G. Move errors left

* Move errors from runtime to compile time wherever possible.
* Prefer type safety, compiler checks, and static guarantees over runtime validation when feasible.
* If a design choice converts a compile-time guarantee into a runtime check, flag it.

### H. Comments

* Code must be understandable without comments. If a comment is needed, consider renaming or decomposing first.
* A comment is only justified to explain something the code is structurally incapable of expressing — a known bug workaround, a regulatory constraint, a non-obvious invariant.
* Bad comments are worse than no comments.
* Ceremonial header comments are forbidden — authorship and change history belong in source control.
* **Exception:** AI context headers are permitted where necessary to establish hot-path status or invariants that cannot be inferred from the diff alone.

---

## 6) P2 minors (nice to fix)

* Naming, local clarity, consistent style with the repo.
* Minor allocations or micro-optimizations in non-hot paths.
* Small simplifications that do not alter behavior.

---

## 7) Evidence requirements ("prove it")

### Tests

* Unit tests for behavior changes.
* Integration tests for DB-facing changes.
* Regression tests for bug fixes.
* Tests should primarily verify observable behavior, not internal implementation. Tests that assert internal method calls or internal structure instead of observable outcomes are fragile and should be flagged.
* **Adversarial and boundary inputs are required** for any test touching string handling, identifier generation, SQL building, or type mapping.
  * Make no assumptions about input size or content. Where a developer puts `"foo"`, a user will put `"supercalifragilisticexpialidocious"` — or a 64KB string, a null, a reserved word in one or more dialects, a string containing a parameter marker, a Unicode edge case, or a value at the boundary of the underlying type.
  * Minimum bar: test with values that are longer, stranger, and more hostile than any example in the implementation.
* Test names must express intent. A well-named test is documentation that cannot go stale. `Test1` is not a test name.

### Performance

Required when:

* PR claims perf improvement.
* PR changes pooling, container execution, mapping, or dialect hot paths.

Requirements:

* BenchmarkDotNet with correct warmup.
* Cold-path and steady-state distinguished where relevant.
* Microbenchmarks must isolate the code under test.
* Delta must exceed noise floor.

### Threat note (security changes)

Required when touching: authn/authz, token handling, SQL building, deserialization, filesystem paths, logging, connection string handling.

Threat note format (5 bullets):

* Entry points
* Trust boundaries
* Assets at risk
* Attacker goals
* Mitigations and tests

---

## 8) Waivers

* **P1 majors** may only be waived when the PR author demonstrates the finding is a **false positive** — the rule does not apply given the actual context. Deferral is not a waiver; deferred concerns require a follow-up issue.
* **P0 blockers cannot be waived.**

---

## 9) DB impact notes

If SQL/dialect behavior changes, reviewers must state:

* Affected DBs (at least by family: Postgres-like, MySQL-like, SQL Server-like, embedded, warehouse)
* Expected behavior differences
* Which integration tests cover it, or what new test is required

---

## 10) Reviewer personas (how to think)

* **Boundary:** Where does control transfer? Where does ownership transfer? Is every boundary explicit and controlled?
* **Ownership:** Who owns this resource? Who releases it? Can ownership escape the scope where it is controlled? Can two components believe they own it?
* **Security:** Assume hostile inputs. Insist on invariants and tests. Silence is failure. Secrets go nowhere.
* **Runtime reality:** What does this actually do at runtime? Are there hidden allocations, blocking calls, implicit IO, or other invisible costs?
* **Behavior locality:** Does behavior live with the structure that owns the invariant? Are objects exposing capabilities or leaking data?
* **Design/Testability:** If it is hard to test, the design is wrong.
* **Clarity:** Code should read like an executable spec. One statement per line. Braces everywhere. Explicit parentheses. If you cannot say it in English, you cannot say it in code.

---

## Notes (repo-specific)

This policy aligns with existing repo mandates:

* TDD required, integration suite required, ValueTask hot paths, TransactionScope ban, interface-first, multi-dialect correctness constraints.

Influences: Abrash (measure everything, know the machine, expose hidden costs), Holub (behavior lives with the owning structure, design for testability, interfaces over concrete types), Lampson (explicit ownership, every resource has a single clear owner), Martin (small cohesive functions, single responsibility, clean contracts, dependency direction), Schneier (hostile inputs, explicit boundaries, fail loudly, secrets stay secret).

---

## 11) Enforcement Mechanisms

Compliance with this policy is enforced via:

1. **GitHub PR Template (`.github/PULL_REQUEST_TEMPLATE.md`)**: Reminds contributors of the P0 hard-bans and required standards.
2. **Copilot Instructions (`.github/copilot-instructions.md`)**: Configures GitHub Copilot Code Review to use this policy as its primary system prompt for PR analysis.
3. **CI Checks (`.github/workflows/deploy.yml`)**: Automated `grep` checks in the build pipeline to catch P0 violations like `TransactionScope` usage before merge.
4. **Manual Peer Review**: All PRs require approval from a maintainer who verifies adherence to this policy and project-specific invariants.


