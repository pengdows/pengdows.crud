## Summary
<!-- Describe your changes in detail -->

## Motivation
<!-- Why is this change required? What problem does it solve? -->

## Reviewer Checklist
<!-- Use this as a guide for self-review before asking for a merge -->

- [ ] **I have reviewed [REVIEW_POLICY.md](../REVIEW_POLICY.md)** and my changes comply.
- [ ] **No `TransactionScope`** (use `ctx.BeginTransaction()`).
- [ ] **No String Interpolation in SQL** (use `SqlContainer`).
- [ ] **Braces everywhere** (even single-line `if` blocks).
- [ ] **One statement per line** (no `if (x) return;`).
- [ ] **No `else` after `return/throw`**.
- [ ] **Hot paths use `ValueTask`** (minimize allocations).
- [ ] **Public APIs are in `.abstractions`**.
- [ ] **Nothing new is public unless it needs to be**: every new public/protected member is listed in `PublicAPI.Unshipped.txt` with a reason, and no raw `DbConnection`/`DbCommand`/`DbDataReader`/`DbTransaction`/`DbDataSource` appears in a public output.
- [ ] **All supported DBs verified**: changes tested/verified against every `SupportedDatabase` value that the change can affect (the integration run covers the always-on set).
- [ ] **TDD First**: Tests included to reproduce fix or verify feature.

## DB Impact Notes
<!-- Note any performance, session settings, or provider-specific impacts -->
