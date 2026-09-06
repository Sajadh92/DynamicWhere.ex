# Phase 7 — Platform

**Branch:** `feat/policies-v3`
**Roadmap:** [2026-08-22-policies-v3-roadmap.md](2026-08-22-policies-v3-roadmap.md)
**Spec:** [2026-08-22-dynamicwhere-policies-design.md](../specs/2026-08-22-dynamicwhere-policies-design.md)
**Written:** 2026-09-06, against the code Phase 6 left behind — not against the roadmap's sketch.

**Exit criteria, from the roadmap:** explain returns the decision chain with sources and overrides;
sealed fields absent from `/schema` and rejected by `POST /rules`; simulate returns a sanitized
filter without executing.

---

## What this phase is

`DynamicWhere.ex.Policies.AspNetCore` — the fourth package. Plus the four attributes the admin
surface exists to expose (`[DwDescribe]`, `[DwAllowedValues]`, `[DwCost]`, `[DwAudit]`), the cost
cap `[DwCost]` feeds, the audit sink `[DwAudit]` writes to, and the outbound half of `[DwAlias]`
that Phases 3 and 4 each deferred here by name.

The endpoints are thin. **Everything they answer with is computed in the core package**, for two
reasons that are not stylistic:

- `/schema` has to enumerate a type's fields, and the walk that decides what a valid field path is
  lives behind `CacheReflection`, which is `internal`. A second walk in the AspNetCore package
  would be a second definition of "field" — and the sanitizer's own comment on `TryValidate<T>`
  says exactly why that is the failure mode to avoid: *"a second copy is exactly how the two
  spellings drift apart"*. A schema that advertises a field the query then refuses, or omits one
  it accepts, is that drift with an HTTP interface on it.
- `/explain` has to report which fragment won and which lost. Precedence is `PolicyResolver`'s, and
  reimplementing four comparison rules in an endpoint gives an operator a decision chain that can
  disagree with the decision. The resolver gains an explain mode instead.

So: core gains schema, explain, simulate, cost and audit; the package gains HTTP, claims, DI and
authorization. A host with no ASP.NET Core gets all of the former.

---

## Decisions settled before implementation

### 1. The admin API refuses to map without a named authorization policy

`MapDwPolicyAdmin` takes options naming an ASP.NET authorization policy for reads and a **separate**
one for writes. Omitting either throws at startup. There is an explicit opt-out that has to be
written by hand.

`POST /rules` changes what every caller in the application may see. A default that works without
configuration is a default that ships open, and a convention-named policy (`"DwPolicyAdmin"`) only
fails when someone calls the endpoint — which, for an endpoint nobody is supposed to call, is never,
until the wrong person does. Read and write are separate because the population who may *inspect* a
policy is not the population who may *change* one.

### 2. Rules may set all four new facts, and `Overridable` is the ceiling

Rules can set label, description, group, order, allowed values, cost weight and audited features,
exactly as they can already set an alias or a transform. The existing sealed/overridable ceiling
does the work: an attribute is sealed by default, so `[DwCost(10)]` cannot be undercut by a rule
unless its author wrote `Overridable = true`.

This is the cheaper answer than it looks, because all four ride **inside the `detail` document that
already exists** — the EF `Detail` column and the Redis rule JSON both carry it. No new columns, no
migration, nothing for a consumer's schema to grow.

It is not free. Two of the four are enforcement rather than decoration: a cost weight dropped in
transit under-charges the query, and a dropped audit flag means the access happened and no record
was written. Both fail open, both quietly. So all four are round-tripped through
`PolicyRuleDocument` and **each is mutation-checked on its own** — dropping it from the writer must
turn the suite red by itself. That is the discipline Phase 6 established for `Forced`,
`RequiredOperators` and `AllowedOperators`, and the reason it exists applies here unchanged.

**The five descriptive facts are elected independently, not as a block.** `[DwDescribe]` and
`[DwAllowedValues]` are two attributes on one property, and a single winner-takes-all election
between them would let the one that wins erase the other. Each fact elects through its own
`Best(...)` scan, the way `ElectAlias` and `ElectRequired` already do.

**Ties break toward the stricter value.** Two equally-ranked fragments carrying different cost
weights elect the higher; two disagreeing on audit elect audited. Election by rank alone would fall
through to `Effect`, which means nothing on a fragment that only carries a cost.

### 3. Audit events buffer on the context and drain once per request

`IDwAuditSink.Write` is asynchronous; `ToList` is not. The query path appends `DwAuditEvent`s to a
buffer on `DwPolicyContext`; the AspNetCore middleware awaits the sink after the response; a host
outside ASP.NET calls `DwPolicy.DrainAuditAsync(ctx)` itself.

Fire-and-forget loses records on process exit and turns a failing sink into an unobserved exception
— for a security record, that is the wrong failure. Blocking the sync path on the sink puts I/O on
the query path and deadlocks in any host with a synchronization context.

### 4. Outbound alias renaming ships, on the dynamic surface only

Phase 3 deferred it to Phase 4, Phase 4 deferred it here by name, and Phase 9 is release
preparation — deferring again means `[DwAlias]` ships inbound-only in v3.0 and `/schema` advertises
names that no result ever carries.

`ToListDynamic`, `ToListAsyncDynamic`, `Group` and `Summary` rebuild their materialized rows with
aliased keys, **and only when the caller's resolved policy actually aliases a field the query
projected**. With no alias in play the pipeline's own output is returned untouched, so the
unguarded shape is unchanged and so is the guarded shape for every caller who has no aliases.

`FilterResult<T>` keeps `T`'s property names. Nothing can change that — `T` is the caller's type —
and it is documented rather than worked around.

---

## What the code already provides, and what it does not

Reviewed against the tree at `5ba4fbd`, not against the roadmap.

| Needed | State |
|---|---|
| `StorePolicyProvider.Version`, `.IsDegraded` | Public. Ready for `/health`. |
| Snapshot load time, last error | **Private.** `_loadedAt` and the failure are not exposed; `/health` reports "age" and "last error" per §5.7. Both must be published. |
| `SealedFields.Refuse` | Public and shared by all three stores. `POST /rules` inherits it. |
| `PolicyResolver.Resolve` | Public, but `Sources` holds **winners only** — one per feature. Losers are discarded, so `/explain`'s "Overrode" and "Ignored" lines have no input. Needs an explain mode. |
| `FilterSanitizer.Sanitize` | **`internal`.** `/simulate` cannot reach it from another assembly. Needs a public simulate entry point rather than `InternalsVisibleTo`. |
| `GET /rules?subject=` | No new store surface needed: broad subjects filter the pinned snapshot, `User:` reads `LoadNarrowAsync`. |
| `CreatedBy` / `UpdatedBy` | Round-trip through both stores, always null. The admin surface is the first place a principal is known. |
| `DwCaps` | Has `MaxPageSize`, `MaxConditions`, `MaxOrderFields`, `MaxNavigationDepth`. **No `MaxQueryCost`** — §4.6 names it and it does not exist. |
| Friendly entity names | Nothing. Rules match on `FullName`; `/schema/{entity}` needs a registry of exposed types, which doubles as the argument list `ValidateModel` already takes. |

**The entity registry is a security boundary, not a convenience.** Without one, `/schema/{entity}`
resolving a type by name lets an operator enumerate any type in any loaded assembly. Exposed types
are declared at startup.

---

## Tasks

Each ends with a commit. TDD throughout: the failing test is written and seen to fail first.

- [ ] **1. Plan.** This document.
- [ ] **2. The four facts, from attributes.** `[DwDescribe]`, `[DwAllowedValues]`, `[DwCost]`,
      `[DwAudit]`; `FieldFacts` carried on `PolicyFragment`; the seven flattened onto `FieldPolicy`;
      `AttributePolicyProvider` emits them; per-fact election in `PolicyResolver`, ties to the
      stricter value; `PolicyModelValidator` additions.
- [ ] **3. The four facts, from rules.** `PolicyRule` carries them; `PolicyRuleDocument` round-trips
      them inside the existing `detail` document; all three stores exercise them in the conformance
      suite; each fact mutation-checked separately.
- [ ] **4. `[DwCost]` enforcement.** `DwCaps.MaxQueryCost`; the sanitizer charges every field
      reference across Where, Order, Select, Group, Aggregate, Segment and Summary; new
      `PolicyErrorCode.QueryCostExceeded`; the refusal names the budget and the total, and the trace
      records it.
- [ ] **5. `[DwAudit]` and `IDwAuditSink`.** `DwAuditEvent`; the buffer on `DwPolicyContext`;
      `DwPolicy.DrainAuditAsync`; emission wherever an audited field is touched, on the allowed path
      as well as the refused one.
- [ ] **6. Schema, in core.** `PolicySchema` / `PolicySchemaField`; the entity registry and its
      friendly names; the field walk through `CacheReflection`, bounded by `MaxNavigationDepth`;
      sealed fields omitted; per-caller, so a denied field is reported as denied and a masked one as
      masked.
- [ ] **7. Explain, in core.** `PolicyResolver.Explain`; per feature the winner, what it outranked,
      and whether anything tied it — the Phase 1 Task 11 attribution note, closed by reporting every
      tied source rather than the arbitrary winner.
- [ ] **8. Simulate, in core.** A public entry point that sanitizes a `Filter`, `Summary` or
      `Segment` against a caller and returns the sanitized clause and the trace, executing nothing.
- [ ] **9. Outbound alias renaming** on the four dynamic-shaped methods.
- [ ] **10. The package.** `DynamicWhere.ex.Policies.AspNetCore`; `DwPolicyContext.FromClaims`;
      the async context factory; DI registration for all three stores.
- [ ] **11. The endpoints.** All seven of §5.7, the two authorization policies, and the audit-drain
      middleware.
- [ ] **12. Release plumbing.** Solution, `publish.yml` pack step, `build/check-version.ps1` — in
      this phase, because a package that builds and is never packed fails silently.
- [ ] **13. The reading pass.** Twenty fail-open defects across six phases, none of them found by a
      test that already existed. Read the diff for the twenty-first.
- [ ] **14. Close.** Roadmap outcome, memory.

---

## Standing rules that bind this phase

- No edits to `Builder.cs`, `Validator.cs`, `Converter.cs`, `Normalizer.cs`. Verified before close.
- Explicit pathspecs when staging. Never `git add -A`.
- Nothing pushed. Merging to master publishes to NuGet and redeploys the docs site.
- Mutation-check anything security-relevant: break it deliberately, confirm the suite goes red.
