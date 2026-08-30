# Schema Management: Today's Boundary and the Future Executor Concept

`pengdows.crud` follows a database-first philosophy: the database is the source of truth, and the
application consumes a contract derived from it, not the other way around (see
[`positioning/product-thesis.md`](./positioning/product-thesis.md), principle 1). That has a direct
consequence for schema management: **the core library does not manage schema at all.** It has no
migration API, no "create tables from my entities" convention, and no code path that issues DDL.
Schema-related tooling lives entirely in a separate, adjacent project — `pengdows.poco.mint` — and
even there, what ships today is much narrower than an idea that has been discussed for that
ecosystem: a generalized, DBA-governed schema executor. This doc draws the line between the two
precisely, so neither gets overclaimed.

## What ships today: schema inspection and POCO adoption

`pengdows.crud` itself never inspects, creates, alters, or drops schema objects. Its only contract
with the database is the SQL a caller writes and the `[Table]`/`[Column]`/`[Id]`/`[PrimaryKey]`
attributes a caller puts on a POCO.

Schema *inspection* is a separate, real, shipped capability — but it lives in
[`pengdows.poco.mint`](https://github.com/pengdows/pengdows.poco.mint), not in `pengdows.crud` or
`pengdows.crud.abstractions`. It:

- Connects to an existing database and reads its schema (tables, columns, types, keys, foreign
  keys, unique constraints) via `DatabaseInspector` (`core/DatabaseInspector.cs`), which is built on
  the same `IDatabaseContext`/`ISqlDialect` machinery described in
  [`capability-discovery.md`](./capability-discovery.md), not a reimplemented per-database
  introspection layer.
- Generates C# POCOs annotated with the correct `[Table]`, `[Column]`, `[Id]`, `[PrimaryKey]`,
  `[Version]`, and audit attributes for those tables.
- Ships two real, versioned interfaces to that same inspector: the `pengdows.poco.mint.cli` NuGet
  package (CI/CD path — schema in, generated `.cs` files out) and a Dockerized Svelte web UI
  (`pengdows/pengdows.poco.mint` image) for a DBA-driven, no-C#-required workflow — connect, browse
  the schema, select tables, download a versioned ZIP of POCOs.

That is the entire boundary of what exists today. It is strictly **one-directional and read-only
against the target database**: schema → generated code. There is no target-schema comparison, no
diff, no generated DDL, no execution against the database, and no dependency ordering of changes.
Inspecting metadata is not the same problem as planning or applying a schema change, and nothing
shipped today attempts the latter.

## What pengdows.crud will never do implicitly

This boundary is intentional, not a gap waiting to be closed by a future release of the core
library:

- No automatic migrations at application startup or on first use of an entity.
- No convention-based "create the table if it's missing" behavior driven by attributes — the
  opposite of a code-first ORM's schema ownership model.
- No hidden DDL execution anywhere in `TableGateway<TEntity, TRowID>`,
  `PrimaryKeyTableGateway<TEntity>`, or `DatabaseContext`.

If a schema change needs to happen, a human decides that and runs it — the library's job stays
confined to talking to the schema that already exists.

## The future generalized schema executor (design concept, not implemented)

A more ambitious idea has been discussed for the broader ecosystem around `pengdows.crud` and
`pengdows.poco.mint`: a generalized schema executor that could take a database from its current
state to a desired target state, safely and across providers. **None of this is built.** It is
recorded here as a design concept — tracked as `DOC-010` in
[`planning/future-work.md`](./planning/future-work.md) — so that anyone evaluating the project can
see the intended shape of the idea without mistaking it for a shipped feature.

The concept is a pipeline of distinct stages:

| Stage | Purpose | Status |
|---|---|---|
| 1. Inspect existing schema | Read the current state of the target database | **Shipped** — `pengdows.poco.mint`'s `DatabaseInspector` |
| 2. Target desired schema | Express the schema the caller wants (e.g. from a set of POCOs, or a declared schema definition) | Not implemented |
| 3. Diff | Compute the difference between existing and target schema | Not implemented |
| 4. Provider-specific adjustment | Translate the diff into syntax and constraints correct for the connected engine's dialect | Not implemented |
| 5. Dependency ordering | Sequence the resulting changes so foreign keys, views, and indexes are created/dropped in a safe order | Not implemented |
| 6. Produce reviewable DDL | Emit the generated DDL as a script for a human to read — not execute it | Not implemented |
| 7. Execute | Apply the reviewed DDL against the database | Not implemented |
| 8. Verify | Re-inspect the schema and confirm the applied result matches the target | Not implemented |

Only stage 1 exists today, and only in service of POCO generation — it does not feed into any of
stages 2–8, because those stages have no implementation to feed into. Do not read the existence of
schema inspection as evidence that diff planning, dependency ordering, executable DDL generation,
or post-execution verification already exist in some partial form; they do not.

## DBA authorization is a hard requirement, not a nice-to-have

Whenever this concept moves from design to implementation, two constraints are non-negotiable —
they follow directly from the project's stated goal of being "the DAL a DBA insists on"
([`positioning/product-thesis.md`](./positioning/product-thesis.md)) rather than a tool that treats
schema as something an application is entitled to change on its own:

- **No auto-apply, ever.** Stage 6 (produce reviewable DDL) and stage 7 (execute) must remain
  separate, explicit actions with a human review checkpoint in between. A generalized schema
  executor that silently applies its own diff — the way a code-first ORM might create or alter
  tables on startup — would be a direct contradiction of the "database is the source of truth"
  principle this project is built on. The DDL a diff produces is a proposal for a DBA to read,
  not an instruction the tool is entitled to carry out on its own.
- **An owned-object boundary.** The tool must operate only against objects it created or was
  explicitly told it owns, and must never alter or drop a table, column, constraint, or other
  object outside that declared boundary — even if a diff against a target schema would technically
  call for it. A shared database can and often does contain objects owned by other applications,
  hand-maintained DBA schema, or systems this tool has no visibility into; discovering a difference
  is not authorization to act on it. Any real implementation needs an explicit ownership
  declaration mechanism (however it ends up being expressed) that stages 3–7 respect as a hard
  filter, not a best-effort heuristic.

## Status

This document exists to prevent two specific overclaims: that `pengdows.poco.mint`'s schema
inspection already constitutes migration tooling, and that a generalized schema executor is a
current or near-term shipped capability. As of this writing it is a roadmap design item only —
tracked in [`planning/future-work.md`](./planning/future-work.md) — with no diff engine, DDL
generator, execution path, or verification step implemented anywhere in this repository or in
`pengdows.poco.mint`.
