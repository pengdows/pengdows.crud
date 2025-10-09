# AI Review Follow-up – Fluent Builder Suggestion

The automated review proposed adding a fluent SQL builder on top of `SqlContainer` and related APIs. After evaluating the
suggestion we are declining to pursue it for the 1.1 line.

## Why we are not adding a fluent builder

- **Risk of abstraction drift** – `SqlContainer` already enforces parameterization and dialect-aware quoting. A new fluent layer
  would introduce a second abstraction that needs to stay feature-parity with the container pipeline and every dialect
  capability. The maintenance cost is high for minimal value.
- **Complexity vs. payoff** – The proposed builder mostly wraps raw SQL strings, so it does not substantially improve type
  safety beyond what our existing helpers provide. We would also need to model advanced features (bulk operations,
  partition hints, temporal tables, etc.), which multiplies the API surface with little measurable benefit.
- **Testing overhead** – Our contribution guidelines require positive and negative coverage for each code path. A fluent
  builder would demand a large matrix of tests across dialects and query shapes, adding ongoing cost without directly
  supporting the release goals.
- **Clean code priority** – We prefer keeping the execution pipeline small and explicit. Adding another façade would obscure
  the existing entry points and make the system harder to reason about for contributors.

## Path forward

- Continue investing in `SqlContainer` and the planned execution-pipeline refactor (CommandPlan → PreparedCommand →
  ResultMapper<T>).
- Document advanced query patterns (CTEs, window functions, bulk operations) using the existing container APIs instead of a new
  DSL.
- Revisit a higher-level builder only if a concrete scenario demonstrates significant safety or productivity gains that cannot
  be satisfied with the current primitives.

This note lives in `docs/review/` so future review cycles can see which suggestions were considered and why they were declined.
