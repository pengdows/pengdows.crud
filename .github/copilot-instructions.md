All Copilot code reviews for this repository MUST adhere to the standards defined in the canonical review policy.

The full review policy is embedded below:

---
# REVIEW_POLICY.md (canonical)

Use this as the single source of truth for Claude/Codex/Gemini/Copilot reviews.

---

## 1) Scope rules

* Review **only the PR diff** plus any code directly referenced by the diff.
* Do not propose unrelated refactoring, "cleanup," or cosmetic changes outside the diff.
* Be concise. Focus on P0 (Blockers) and P1 (Majors).

## 2) Severity Levels

| Level | Definition |
|-------|------------|
| **P0: Blocker** | Hard-ban violations, incorrect transaction semantics, connection leaks, or security vulnerabilities. Must be fixed before merge. |
| **P1: Major** | Significant performance regressions on hot paths, non-idiomatic public API changes, or poor test coverage. |
| **P2: Minor** | Naming improvements, documentation typos, or small optimization opportunities. |

## 3) The Hard Bans (P0)

If you see these, it is a hard "DO NOT MERGE":

* **TransactionScope is forbidden** — use `BeginTransaction`.
* **String Interpolation in SQL** — use `SqlContainer` or `AddParameterWithValue`.
* **Missing Braces** — `if`, `else`, `while`, `for` must ALWAYS use braces `{}`.
* **One Statement per Line** — do not use `if (cond) return;` on a single line.
* **Else after Return/Throw** — if the `if` block returns or throws, do not use `else`. Just continue the method.
* **Leaking Connections** — every connection checkout must be disposed (prefer `await using`).
* **Secrets in Code** — no hardcoded keys, passwords, or real connection strings.

## 4) Engineering Standards

* **TDD First** — Every bug fix or feature must have a corresponding test that reproduces the issue or verifies the feature.
* **ValueTask on Hot Paths** — Hot-path methods (hydration, reader loops) must return `ValueTask` to minimize allocations.
* **Interface-First** — Public APIs must be in `pengdows.crud.abstractions`.
* **Multi-Dialect Correctness** — Changes to SQL generation must work correctly across all 13 supported providers.

## 5) The Review Format

Always use this exact template for the summary:

1. **MERGE:** YES/NO
2. **Blockers (P0):** List violations or "None"
3. **Majors (P1):** List issues or "None"
4. **Minors (P2):** List suggestions or "None"
5. **Missing evidence:** List missing tests/benchmarks
6. **DB impact notes:** Brief summary of performance or provider-specific impacts
7. **Minimal patch guidance:** Clear, concise instructions for the developer to fix blockers
8. **Confidence:** LOW/MEDIUM/HIGH

## 6) Philosophical Alignment

Always align with these experts:
Hoare (null safety, type safety), Goetz (concurrency safety, immutability, thread-safety), Hickman (explicit ownership, every resource has a single clear owner), Martin (small cohesive functions, single responsibility, clean contracts, dependency direction), Schneier (hostile inputs, explicit boundaries, fail loudly, secrets stay secret).
