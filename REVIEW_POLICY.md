REVIEW_POLICY.md (canonical)

Use this as the default review policy for human and automated code reviews. Repository-specific rules may extend it through a documented project overlay, but may not weaken its P0 requirements.

1) Purpose and scope

A review must determine whether a change is correct, safe, maintainable, and supported by enough evidence to merge.

Review:

The proposed diff.

Code, configuration, schemas, tests, and documentation directly affected by the diff.

Callers, implementations, contracts, and operational paths needed to evaluate the change.

Do not:

Turn the review into an unrelated refactor.

Review untouched code unless the change activates or depends on an existing defect.

Require a preferred pattern when the submitted design is correct and no material risk exists.

Comment on generated code unless the generator, generated contract, or runtime behavior changed.

Report formatting-only concerns already enforced by an automated formatter or linter.

Prefer the smallest change that fixes the problem, preserves valid contracts, and leaves the system easier to reason about.

2) Review output contract

Every review must contain, in this generation order:

Blockers (P0) — defects that prohibit merge

Majors (P1) — material defects that should be fixed before merge

Minors (P2) — optional improvements and local clarity issues

Missing evidence — tests, measurements, compatibility checks, or threat analysis still required

Impact notes — only for affected technical or operational domains

Minimal patch guidance — concrete correction, not a redesign

REVIEW STATUS: COMPLETE / PARTIAL

MERGE: YES / NO / UNDETERMINED

Confidence: HIGH / MEDIUM / LOW

The verdict must not be the first conclusion generated. An automated reviewer may reason before producing visible output, generate the findings before the verdict, or let tooling construct and place a summary header after analysis. A reviewer that streams its first generated conclusion directly must generate findings before the verdict.

Review status is scope-relative:

COMPLETE — the reviewer accessed and evaluated everything that Section 1 places in scope. This does not require a repository-wide review. A diff-only review may be complete when the change does not require external callers, contracts, configuration, generated output, or operational context to evaluate it.

PARTIAL — one or more artifacts required by Section 1 were inaccessible, omitted, or intentionally sampled.

Verdict precedence is:

A supported P0 produces MERGE: NO, regardless of review status.

If no P0 is established, PARTIAL status, an unresolved credible high-impact risk, or inaccessible evidence required to decide safely produces MERGE: UNDETERMINED.

MERGE: YES requires a complete review of the applicable scope, no P0, and no unresolved high-impact Missing evidence entry.

A partial review cannot produce MERGE: YES.

Each finding must include:

Location — file and line or the narrowest applicable scope.

Defect — what is wrong, stated as a falsifiable claim.

Impact — the concrete failure, risk, or maintenance cost.

Evidence — code path, contract, test result, measurement, or documented invariant.

Correction — the smallest viable fix.

Confidence — HIGH, MEDIUM, or LOW.

Confidence means:

HIGH — directly demonstrated by the diff, contract, test, or reproducible behavior.

MEDIUM — strongly indicated, but one relevant fact remains unverified.

LOW — plausible but insufficiently supported. Put low-confidence concerns under Missing evidence. A P0 finding must have at least MEDIUM confidence and evidence establishing the defect.

Every Missing evidence entry must name the specific artifact, test, observation, or measurement needed to resolve it. If the unresolved question represents a credible high-impact risk, the verdict must be UNDETERMINED, not YES.

Do not report a finding merely because code could be written differently. If no failure mode, violated invariant, measurable cost, or material maintenance problem can be stated, omit it.

3) Severity model

Severity follows impact, not reviewer preference.

P0 — blocker

Use P0 when the change can cause one or more of the following:

Incorrect results or corrupted, lost, or irrecoverable data.

A security, privacy, authorization, or secret-exposure failure.

A deadlock, race, leak, unbounded resource use, or uncontrolled resource lifetime.

A broken public contract or incompatible deployment without an explicit migration plan.

A material reliability or availability regression.

An unmeasured change to a required hot path where performance is part of the contract, or a demonstrated material performance regression on a required path.

Verified absence of tests or evidence required to establish safety or correctness.

P0 means MERGE: NO. P0 findings cannot be waived within the normal review process.

Emergency change authority belongs in a separate documented procedure. It must identify the approving authority, accepted risks, rollback plan, tracked remediation, expiration, and post-incident review. Emergency authority may override the normal merge process; it does not downgrade the finding or change the review verdict.

P1 — major

Use P1 for a material design, testability, operability, performance, or maintenance defect that is unlikely to cause immediate catastrophic failure but substantially raises future defect cost or obscures correctness.

A P1 may be dismissed only when evidence shows that the finding is a false positive. Deferring valid work requires a tracked follow-up; it does not make the finding false.

P2 — minor

Use P2 for naming, local clarity, consistency, or small non-critical improvements. P2 findings do not block merge.

Never inflate severity to force a preference.

4) Foundational rules

These rules are review lenses, not slogans. A finding must still identify a concrete defect or missing proof.

Boundary control

Systems fail at boundaries. Every transfer of data, control, authority, ownership, or execution must be explicit and controlled.

Relevant boundaries include:

Processes, services, networks, queues, files, and databases.

Authentication and authorization contexts.

Threads, tasks, callbacks, event handlers, and schedulers.

Parsers, serializers, native calls, plugins, and third-party libraries.

Memory, handles, streams, connections, transactions, and other resources.

Ask what crosses the boundary, who validates it, what failures are possible, and how those failures become visible.

Configuration validity

Configuration is an input boundary. An application must validate its complete effective configuration during startup, before it opens listeners, starts schedulers, consumes messages, or accepts other work.

The startup check must:

Evaluate the final configuration after files, environment variables, command-line arguments, secret stores, and other overrides have been applied.

Validate every setting required by the enabled features and selected operating mode.

Check presence, parsing, type, range, format, mutually exclusive choices, and cross-setting invariants as applicable.

Collect and report all independently detectable configuration errors in one startup attempt instead of failing on the first missing value.

Identify each missing or invalid setting and explain the constraint that failed.

Log setting names or stable configuration paths, never secret values or sensitive derived data.

Terminate startup with a non-zero result when any required setting is missing or invalid.

Optional settings must have explicit safe defaults or documented absence semantics. A default must not conceal configuration required for correct behavior.

Configuration validity and dependency availability are different checks. Validate everything that can be established locally at startup. Use readiness or an explicit dependency policy for transient network or service availability unless the application cannot initialize safely without that dependency.

Ownership — Lampson influence

Every resource and mutable state transition must have a clear owner.

Ask:

Who creates it?

Who may mutate it?

Who releases or completes it?

Can ownership escape its controlled scope?

Can two components believe they own it?

What happens when execution fails halfway through?

Unclear ownership is a design defect. Shared ownership requires an explicit protocol.

Reasonability — Carmack influence

State, control flow, and failure modes must remain obvious enough that a developer can trace cause to effect.

Prefer:

Direct mechanisms over clever indirection.

Local state over ambient or widely shared mutable state.

Explicit transitions over lifecycle magic.

Immediate invariant checks over delayed corruption.

Concrete implementations over speculative generalization.

Flag hidden callbacks, surprising mutation, implicit framework behavior, invalid state combinations, and abstractions that make the actual execution harder to explain. Do not introduce generalized machinery until multiple concrete cases demonstrate the common abstraction.

Runtime reality — Abrash influence

Know what the machine actually does. Verify execution, allocation, copying, blocking, I/O, queries, contention, and complexity instead of inferring cost from source appearance.

Optimization by belief is not evidence. Abstractions do not erase runtime costs.

Adversarial correctness — Schneier influence

Treat external input, timing, state, dependencies, and callers as potentially hostile or malformed. Validate at trust boundaries. Use established cryptographic and security mechanisms. Fail safely and visibly without exposing secrets.

Structural clarity — Martin influence

Keep responsibilities cohesive, dependencies intentional, contracts narrow, and functions focused. Dependency direction should protect policy from volatile implementation details. Complexity must have a specific justification.

Behavioral locality and testability — Holub influence

Place behavior with the type or component that owns the invariant. Expose capabilities rather than leaking state for callers to interpret and modify. Prefer designs whose observable behavior can be tested without reproducing their internal implementation.

Simplicity

Use the simplest design that correctly handles the known requirements and failure modes. Do not solve hypothetical future problems at the cost of present clarity. Simple does not mean incomplete, implicit, or naive.

5) P0 blockers

A. Correctness and state integrity

Block when a change can:

Produce incorrect results for valid, boundary, or adversarial inputs.

Leave state partially applied or internally inconsistent.

Duplicate or lose side effects during retries, cancellation, or partial failure.

Hide a failed operation behind a success result.

Change observable behavior accidentally.

Permit impossible state combinations without enforcing valid transitions.

Check success, failure, cancellation, retry, timeout, and cleanup paths—not only the happy path.

B. Security and privacy

Block when:

Unvalidated data crosses a trust boundary.

Authentication or authorization can be bypassed, confused, or applied inconsistently.

Secrets or sensitive data can reach logs, exceptions, metrics, traces, caches, URLs, or client-visible output.

The change introduces custom cryptography, token formats, signature rules, or security protocols without a compelling requirement and expert review.

Deserialization, query construction, path handling, command execution, templating, or logging allows injection.

Failure handling leaks sensitive context or fails open.

A security-relevant change lacks explicit tests and a threat note.

C. Resource ownership and concurrency

Block when:

A resource can leak on success, failure, timeout, or cancellation.

A resource or transaction outlives the scope that controls it without an explicit ownership transfer.

Shared mutable state lacks synchronization or a documented single-owner rule.

Lock ordering, reentrancy, blocking, or task scheduling can deadlock or starve required work.

Async code performs hidden blocking on a required path.

Cancellation leaves state corrupt or work silently running without an owner.

D. Contracts and compatibility

Block accidental breaking changes to:

Public APIs, protocols, serialized forms, file formats, schemas, or command-line contracts.

Database schemas, migrations, or stored data.

Configuration, environment variables, deployment order, or infrastructure expectations.

Supported platforms, runtimes, providers, clients, or versions.

Intentional breaking changes require explicit scope, migration instructions, versioning where applicable, compatibility evidence, and release documentation.

E. Tests and proof

Behavior changes and bug fixes require tests at the lowest level that proves the observable contract.

Block when:

Behavior changed but no test changed and existing coverage does not demonstrably exercise the new behavior.

A bug fix lacks a regression test that fails without the fix.

Boundary or integration behavior changed without appropriate integration coverage.

A test is disabled, weakened, or rewritten to accept the defect.

Required test failures are ignored or treated as unrelated without evidence.

Required evidence is verified to be absent and the change therefore cannot establish safety or correctness.

Do not demand a unit test for a mechanically verifiable non-behavioral edit. Do demand proof appropriate to the risk.

Reviewer-side blindness is not proof that evidence is absent. When a required artifact cannot be accessed, identify it under Missing evidence, mark the review PARTIAL, and use UNDETERMINED only when that missing artifact prevents a safe verdict. Do not manufacture a P0 from lack of access.

F. Performance claims and regressions

Require measurement when a change:

Claims a performance improvement.

Changes a known hot path.

Alters algorithms, allocation patterns, batching, caching, pooling, serialization, I/O, queries, or concurrency in a way likely to affect cost.

Block when:

A change to a required hot path lacks the measurements necessary to establish that it preserves its performance contract.

The benchmark measures the wrong scope, omits warmup where relevant, mixes cold and steady-state behavior, or reports a delta within noise.

A required path shows a material regression without an accepted tradeoff.

Complexity or memory use becomes unbounded for supported input.

An unsupported performance statement that is not tied to a performance-sensitive change is P1: correct or remove the claim. Unsupported prose alone is not a P0.

G. Observability and operational truth

Block when a change causes logs, metrics, traces, health checks, or status results to report materially false success, failure, latency, counts, or ownership state.

Silent failure is a correctness and security defect. Observability must not expose secrets or become a correctness dependency unless explicitly designed as one.

H. Startup configuration validation

Block an application change when:

A required setting can remain missing or invalid until a later execution path reads it.

The application can begin accepting work before configuration validation completes successfully.

Validation stops at the first independent error when additional configuration errors can be reported safely in the same startup attempt.

Invalid configuration is replaced with a misleading default, ignored, or allowed to produce a later null reference, parse failure, authorization error, data error, or dependency failure.

Startup failure does not identify every detected setting that requires correction and the reason it is invalid.

Configuration diagnostics expose passwords, keys, tokens, connection-string secrets, personal data, or other sensitive values.

Configuration validation behavior lacks tests for valid, missing, malformed, out-of-range, conflicting, and conditionally required settings as applicable.

6) P1 majors

A. Reasonability and control flow (Carmack)

Flag:

State mutations whose source is difficult to locate.

Control flow governed by implicit hooks, callbacks, reflection, interception, or lifecycle behavior when a direct mechanism would be clearer.

Boolean combinations that represent a state machine but permit invalid states.

Shared mutable state with a wider scope or lifetime than required.

Speculative abstractions supported by only one concrete use case.

Indirection that does not remove duplication, protect a boundary, or enforce an invariant.

Missing assertions or invariant checks where corruption would otherwise propagate far from its source.

B. Responsibilities and dependencies (Martin)

Flag:

A function that performs unrelated conceptual operations.

A class or module with multiple independent reasons to change.

Excessive parameters or dependencies that reveal a missing concept or misplaced responsibility.

Dependency direction that couples stable policy to volatile infrastructure.

Inheritance introduced where composition would reduce coupling and preserve clearer contracts.

Public surface area larger than consumers require.

Function length alone is not a defect. Flag size when it obscures control flow, mixes abstraction levels, duplicates policy, or prevents focused testing.

C. Behavioral locality and testability (Holub)

Flag:

"Ask then act" code that retrieves state so another component can enforce the owner's invariant.

Getters or data exposure used primarily to let callers manipulate internal state.

Domain behavior placed in orchestration layers that merely shuttle data between an anemic model and storage.

Tests coupled to private structure, internal call order, or mocks rather than observable behavior.

Designs that require global state, real time, nondeterminism, or external infrastructure when a narrow boundary could make behavior deterministic.

Do not apply this rule mechanically to DTOs, value objects, read models, serialization types, or deliberately procedural code.

D. Hidden runtime costs (Abrash)

Flag work not apparent from the call site, including:

Hidden allocation, copying, parsing, reflection, or materialization.

Accidental repeated I/O, queries, serialization, or enumeration.

Expensive convenience APIs used on hot or high-volume paths.

Unbounded caching, buffering, fan-out, retries, or parallelism.

Algorithmic complexity inconsistent with supported input sizes.

Escalate to P0 when the cost affects a required hot path, blocks async execution, is unbounded, or produces a demonstrated material regression.

E. Error handling and recovery

Flag:

Exceptions caught without adding recovery, translation, or necessary context.

Loss of the original error or stack information.

Retry policies without idempotency, limits, backoff, jitter, or cancellation as appropriate.

Cleanup paths that are complex, duplicated, or untested.

Error messages that prevent diagnosis or incorrectly assign blame.

Fallbacks that conceal degraded behavior.

F. Move errors left

Prefer compile-time constraints, types, schemas, static analysis, and construction-time validation over repeated runtime checks. Flag changes that replace an existing static guarantee with a runtime convention without a concrete benefit.

G. Comments and documentation

Code should express structure, behavior, and intent directly. Comments should explain facts the code cannot express: rationale, external constraints, non-obvious invariants, protocol requirements, or temporary workarounds linked to tracked work.

Flag:

Comments that restate the code.

Stale or contradicted comments.

Ceremonial authorship and change history that belong in source control.

Public contract or operational changes without corresponding documentation.

7) Source clarity rules

Apply these rules where the language supports them. A repository overlay may strengthen them.

Do not report violations already covered by an active formatter, linter, or analyzer unless the automated control failed or the violation exposes a defect the tool does not capture.

Use one executable statement per line.

Use braces or the language's explicit block form for control flow; do not compress control flow until a state-changing statement becomes easy to miss.

After an unconditional scope exit (return, break, continue, or throw), avoid a redundant else when removing it makes the main flow clearer.

Use parentheses when they materially clarify mixed operators or non-obvious precedence.

Prefer early validation and explicit failure over deeply nested happy paths.

Keep names precise enough that comments are not needed to decode ordinary behavior.

Treat an uncovered mechanical violation as P2 by default. Escalate to P1 only when it materially obscures control flow, state mutation, ownership, or intent. Escalate to P0 only when the obscurity creates or conceals a concrete correctness, security, concurrency, or ownership defect.

8) P2 minors

P2 includes:

Naming and local clarity improvements.

Small simplifications that preserve behavior.

Consistency with established repository conventions.

Minor allocations or micro-optimizations outside hot paths.

Documentation or test-name improvements that do not obscure the contract.

Report no more than five individual P2 findings. Summarize additional P2 issues by category and count. P0 and P1 findings have no numerical cap, but consolidate repeated symptoms under their root cause.

9) Evidence requirements

Tests

Require as applicable:

Unit or component tests for behavior and invariants.

Regression tests for bug fixes.

Integration tests for boundaries such as databases, filesystems, networks, queues, providers, frameworks, and external processes.

Contract or compatibility tests for public interfaces and serialized forms.

Concurrency tests for synchronization, cancellation, ordering, retry, and lifetime changes.

Migration and rollback tests for persistent data or deployment changes.

Startup tests proving that valid configuration starts successfully and all independently detectable missing or invalid required settings are reported together before the application accepts work.

Tests should verify observable behavior rather than mirror the implementation. Test names must state the condition and expected result.

Boundary-sensitive code must include hostile and unusual inputs appropriate to the domain, such as:

Empty, null, minimum, maximum, and overflow-adjacent values.

Long strings, Unicode, reserved words, delimiters, control characters, and malformed encodings.

Duplicate, reordered, delayed, partial, and repeated operations.

Timeouts, cancellation, dependency failure, and concurrent access.

Unauthorized, cross-tenant, or privilege-boundary cases.

Performance

Performance evidence must:

Compare the change against a relevant baseline.

Use representative data sizes and workloads.

Isolate the code or subsystem claimed to improve.

Distinguish cold-start and steady-state costs where relevant.

Account for warmup, variance, outliers, and the measurement noise floor.

Include allocation, throughput, latency distribution, I/O, or contention data as appropriate—not only mean elapsed time.

Threat note

Require a threat note for changes to authentication, authorization, identity, tokens, cryptography, query construction, deserialization, command execution, filesystem paths, uploads, secrets, logging, or tenant isolation.

Use this format:

Entry points

Trust boundaries

Assets at risk

Attacker goals and plausible abuse cases

Mitigations and tests

Compatibility and operations

Require explicit evidence when a change affects supported versions, providers, platforms, deployment order, schemas, configuration, feature flags, rollback, monitoring, or capacity.

For application configuration changes, require evidence covering precedence and overrides, enabled-feature requirements, invalid combinations, secret-safe diagnostics, aggregate error reporting, and non-zero startup failure.

10) Impact notes

Include only the affected sections:

API/contract — affected consumers, compatibility, versioning, migration.

Data/database — affected engines or schemas, transaction semantics, migration, rollback, integration coverage.

Security/privacy — trust boundaries, authorization, secrets, retained data, audit behavior.

Concurrency/resources — ownership, synchronization, cancellation, cleanup, capacity limits.

Performance — workload, baseline, measurements, tradeoffs.

Operations — deployment order, configuration and startup validation, observability, rollback, failure recovery.

Platform/provider — affected runtimes, operating systems, architectures, vendors, or versions.

For any affected domain, state expected behavior differences and the evidence that covers them.

11) Repository-specific overlays

Project rules belong in a short repository overlay, such as REVIEW_POLICY.local.md, CONTRIBUTING.md, or an equivalent documented file. The overlay should contain only rules that are genuinely specific to that codebase.

Good overlay content includes:

Supported platforms, runtimes, providers, and versions.

Public API locations and compatibility policy.

Required test suites and commands.

Resource-lifetime or concurrency invariants unique to the architecture.

Forbidden APIs with the reason and approved replacement.

Performance-critical paths and required benchmarks.

Database dialect, schema, migration, or transaction rules.

Generated-code and analyzer policies.

An overlay may raise the severity of a project-specific invariant. It may not downgrade a core P0 or replace evidence with convention.

Example overlay rules—not universal policy—include:

A required async return type on established hot paths.

A ban on ambient transactions in favor of explicit transactions.

A required quoting or parameterization API for generated SQL.

A rule that implementation types remain internal and public consumers use abstractions.

A required integration matrix for supported database engines or providers.

12) Five review passes

Run these as distinct passes. Their overlap is intentional, but their primary questions differ.

Reasonability — Carmack influence
Can a developer trace state, control flow, failure, and cause to effect without guessing?

Runtime reality — Abrash influence
What does this actually execute, allocate, copy, block on, query, or retain under a representative workload?

Adversarial correctness — Schneier influence
What happens when input, timing, state, dependencies, or callers are hostile?

Structural clarity — Martin influence
Are responsibilities, dependencies, functions, and contracts coherent and appropriately narrow?

Behavioral locality and testability — Holub influence
Does behavior live with the invariant it protects, and can observable behavior be proved without coupling tests to implementation?

Apply two system-wide checks across every pass:

Boundary: Where do data, control, authority, ownership, and execution cross?

Ownership: Who owns each resource and state transition through success, failure, cancellation, and cleanup?

Assign each finding to exactly one lens according to its primary failure mode. Other lenses may strengthen the evidence but must not produce duplicate findings. Consolidate related symptoms into their common root cause before generating output.

13) Review discipline

Verify before asserting. Read the relevant contract and call path.

Separate demonstrated defects from questions and missing evidence.

Do not invent requirements that the repository does not have.

Do not confuse unfamiliar code with incorrect code.

Do not approve code solely because tests pass; tests may be incomplete or assert the wrong contract.

Do not reject code solely because a different design is possible.

Prefer one root-cause finding over many symptoms.

Avoid duplicate findings from multiple review lenses.

State uncertainty directly and lower confidence when evidence is incomplete.

Declare a partial review explicitly. Identify both the reviewed scope and the required scope that was not reviewed.

Re-review the corrected path, not only the lines changed in response.

14) Enforcement

Enforce what can be enforced mechanically:

Formatters and linters for syntax and style.

Static analyzers for prohibited APIs, unsafe patterns, dependency rules, and contract violations.

Unit, integration, contract, migration, and performance tests for behavior.

CI policy checks for required evidence and supported matrices.

Human review for architecture, ownership, threat modeling, operational behavior, and whether the evidence proves the intended contract.

Automation should prevent known invalid states, not generate review noise. Every automated rule should identify a real invariant, explain the failure it prevents, and provide a practical correction.

15) Influences

John Carmack — explicit state and control flow, local reasoning, direct mechanisms, invariant checks.

Michael Abrash — measurement, machine-level reality, and visible runtime cost.

Bruce Schneier — hostile inputs, trust boundaries, failure analysis, and conservative security design.

Robert C. Martin — cohesive responsibilities, dependency direction, narrow contracts, and structural clarity.

Allen Holub — behavioral locality, capability-oriented objects, and testable design.

Butler Lampson — explicit ownership, interfaces, and practical system-design boundaries.

These influences provide questions, not appeals to authority. Findings still require evidence.

16) Repository-specific rules (pengdows.crud)

This is the repository overlay described in Section 11, kept in this file rather than a separate document. Nothing here weakens a Section 5 P0; several entries raise severity or add project-specific evidence requirements, which Section 11 permits.

16.1 Hard bans (P0)

TransactionScope is forbidden — use BeginTransaction.

No string interpolation for SQL values — use parameterization.

No unquoted identifiers in custom SQL — use WrapObjectName.

Where available, use pengdows.crud.analyzers to machine-enforce review invariants. Current analyzer coverage includes raw SQL predicate/join value injection (PGC008).

16.2 Core invariants (P1)

ValueTask in hot paths — execution methods return ValueTask/ValueTask<T>; do not regress to Task.

No public constructors on implementation types except DatabaseContext.

Interface-first — consumers depend on abstractions in pengdows.crud.abstractions, not implementation types.

TableGateway extension rule — extend TableGateway to add custom query methods; do not wrap it with a separate service layer.

[Id] vs [PrimaryKey] — never apply both to the same property; preserve documented upsert key priority ([PrimaryKey] first, then writable [Id]).

Dispose readers promptly — ITrackedReader is a lease. No async leaks, no undisposed containers/readers/transactions. No long-lived transactions stored as fields.

SQL correctness and multi-dialect awareness — changes to dialects or SQL generation must consider quoting rules, parameter marker rules, upsert behavior per database, and transaction/isolation semantics. Do not represent the same SQL concept in two different ways simultaneously — duplicate dialect code paths are a correctness and maintenance hazard.

16.3 DB impact notes

If SQL or dialect behavior changes, reviewers must state:

Affected databases, at least by family (Postgres-like, MySQL-like, SQL Server-like, embedded, warehouse).

Expected behavior differences.

Which integration tests cover it, or what new test is required.

16.4 Enforcement mechanisms

Compliance with this policy is enforced via:

GitHub PR Template (.github/PULL_REQUEST_TEMPLATE.md) — reminds contributors of the P0 hard-bans and required standards.

Copilot Instructions (.github/copilot-instructions.md) — configures GitHub Copilot Code Review to use this policy as its primary system prompt for PR analysis.

CI Checks (.github/workflows/deploy.yml) — automated grep checks in the build pipeline to catch P0 violations such as TransactionScope usage before merge.

Manual peer review — all PRs require approval from a maintainer who verifies adherence to this policy and the project-specific invariants above.
