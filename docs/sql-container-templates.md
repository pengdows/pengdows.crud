# SQL Container Templates (`ISqlContainer.Clone`)

`ISqlContainer` exposes two `Clone` overloads for low-cost reuse of an already-built query
structure:

```csharp
ISqlContainer Clone();                          // rebind against the same context/dialect
ISqlContainer Clone(IDatabaseContext? context);  // rebind against a different context/dialect
```

This is the mechanism behind two patterns the rest of the library depends on: cheap re-execution
of the same query with new parameter values, and singleton gateways serving multiple tenants (or
transactions) from one cached query template.

## What gets copied, what gets shared

`Clone` (`pengdows.crud/SqlContainer.cs`) does **not** produce a shallow copy — it builds a new,
independent `SqlContainer` and populates it explicitly:

- **Query text** — the raw SQL builder content (`Query`) is copied into the clone
  (`clone._query.CopyFrom(_query)`). After cloning, appending to one container's `Query` has no
  effect on the other (`Clone_IndependentQueryModifications` in
  `SqlContainerCloningTests.cs` proves this).
- **Rendered command text cache** — if the original has already executed at least once (so its
  parameter-placeholder rendering is cached and still valid), that immutable cached string is
  **shared by reference** with the clone rather than re-rendered — this is the actual performance
  win the template pattern exists for, avoiding a `StringBuilder.ToString()` and a placeholder scan
  on every clone. If the original hasn't executed yet, the clone starts with no cache and renders
  fresh on its own first execution.
- **Parameters** — every parameter is deep-copied into a brand-new `DbParameter` instance created
  through the **target** dialect's own parameter factory (`dialect.CreateDbParameter(...)`), not
  copied by reference. Name, `DbType`, value, direction, size, scale, and precision are carried
  over (non-default properties only, to avoid unnecessary provider overhead). Mutating a
  parameter's value on the clone (`SetParameterValue`) never affects the original, and vice versa
  — proven by `Clone_IndependentParameterModifications` and `Clone_PreservesParameterProperties`.
- **The `WHERE`-appended flag, parameter sequence, and rendered-parameter map** — copied so the
  clone's own rendering behaves identically to what the original had already established.

## Cross-dialect rebinding: what actually gets re-rendered

`Clone(context)` uses the **target** context's dialect (`context.GetDialect()`) to construct the
clone and every one of its cloned parameters. Two different things are affected by this,
differently:

- **Parameter markers** (`?`, `@name`, `:name`, `$1`, …) — these ARE dialect-aware and get
  re-rendered correctly for the target dialect, but only if the clone renders its command text
  fresh. Query text is built with a neutral `{P}name` placeholder token, replaced with the actual
  dialect-specific marker (`RenderParams`) at first execution — and that replacement runs using
  the container instance's *own* dialect, so a clone targeting a different dialect than the
  original produces the correct marker style for its target. `Clone_MultiTenantScenario_PreventsCrossTenantDataLeakage`
  and `Clone_CachedTemplateScenario_WorksWithDifferentTenants` prove this across
  SQLite/PostgreSQL/DuckDB parameter-marker styles.
  - **Caveat:** if the *original* container had already executed (and therefore already cached its
    rendered command text with the original dialect's markers baked in) before you clone it, that
    cached text is shared with the clone as-is, markers and all — see "What gets copied, what gets
    shared" above. **Clone a template before its first execution** if you intend to reuse it across
    dialects with different parameter-marker styles, so each clone renders its own markers on its
    own first execution instead of inheriting an already-baked-in set.
- **Identifier quoting** — `WrapObjectName()` calls (used to quote table/column names) run once, at
  *build* time, before the query text ever reaches `Clone`. Their output is already baked into the
  raw query text and is **not** re-rendered per target dialect on `Clone` — cloning across dialects
  reuses whatever quoting characters the original build produced. In practice this is not a
  portability hazard today: every currently-shipped dialect uses the same ANSI double-quote
  (`"name"`) policy (see CLAUDE.md's "`WrapObjectName` uses ANSI double-quote identifier quoting as
  the enforced default policy across every currently supported dialect"). It would only become a
  real hazard if a future dialect legitimately needed different quoting (CLAUDE.md's own carve-out
  example is a hypothetical engine like MS Access) — a container built against such a dialect and
  cloned to a "normal" ANSI dialect (or vice versa) would carry the wrong quote characters.

## The template pattern

A singleton gateway that needs to serve many tenants (or re-execute the same shape of query
repeatedly with fresh values) builds one query once against a "template" context, then clones it
per call against the real operation context:

```csharp
// Built once, e.g. lazily cached inside a gateway
var template = templateContext.CreateSqlContainer(
    "SELECT id, name, email FROM users WHERE id = ");
template.AddParameterWithValue("p0", DbType.Int32, 0); // placeholder value, never executed as-is

// Per call, per tenant:
using var container = template.Clone(tenantContext);
container.SetParameterValue("p0", actualUserId);
var result = await gateway.LoadSingleAsync<User>(container);
```

Because the clone's parameters are independent `DbParameter` instances, concurrent clones from the
same template are safe to use simultaneously — there is no shared mutable parameter state between
them. This is exactly the mechanism `BaseTableGateway`'s internal reuse of compiled query
structures relies on to stay both fast (shared rendered SQL text) and safe (independent parameters
and independent per-clone dialect binding) across a singleton gateway shared by every tenant.

## Transaction use

Cloning against a `TransactionContext` works the same way — `context.GetDialect()` resolves to the
transaction's own dialect, and the clone's connection acquisition, once executed, goes through the
transaction's pinned connection like any other container created against it. `Clone_WithTransactionContext_UsesTransactionDialect`
covers this. There is nothing transaction-specific about `Clone` itself; the transaction-vs-context
distinction is handled the same way it is for any other `ISqlContainer` — by which `IDatabaseContext`
(ordinary context or `TransactionContext`) you pass in.

## Disposal

The original and its clones are fully independent, separately disposable objects — disposing the
original does **not** invalidate a clone still in use, and vice versa. `Clone_DisposedOriginal_CloneStillWorks`
disposes the original immediately after cloning and then successfully executes queries against the
still-valid clone. Always dispose (or `await using`) every container you create, template and
clones alike — a clone is a fully independent object holding its own parameters, not a lightweight
view over the original.

## Safe singleton patterns

- Build the template container once (e.g. lazily, cached on first use) against a stable "template"
  context whose dialect matches whichever database you expect most callers to target — or against
  any context, if you always clone before first execution and only care about parameter-marker
  correctness (see the caveat above).
- Never execute the template container itself if you plan to keep cloning it across different
  dialects afterward — executing it first caches dialect-specific rendered text that later clones
  would inherit unchanged.
- Treat the template as read-only after construction from the caller's perspective: always clone
  before setting parameter values or appending to `Query`, never mutate the shared template
  in-place from concurrent callers.
- One template per distinct query shape is enough for a singleton gateway to serve unlimited
  tenants/contexts — this is the mechanism, not row-level filtering or per-tenant SQL generation,
  that lets one gateway instance safely serve every tenant in a multi-tenant deployment (see
  `docs/connection/multitenancy.md`).
