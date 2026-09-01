# Policies v3.0 — Phase 5: Dynamic store — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Policy stops being a property of the source code. A rule written to a store — granting a role a masked view, scoping a tenant, expiring a contractor's access on a date — reaches the resolver as a fragment and competes under the same precedence the attributes already do, without ever being able to loosen a sealed one.

**Architecture:** Step 0 of the sandwich, off the query path entirely. A store loads an immutable snapshot; a background refresh swaps it atomically; `StorePolicyProvider` turns the rules that match a caller into fragments and hands them to the resolver that already exists. Nothing in the sanitizer or the transformer changes.

**Tech Stack:** .NET 6 library, xUnit on net8.0. `System.Text.Json` is in the net6.0 shared framework, so the payload parser adds no package reference and the core package stays dependency-clean.

**Branch:** `feat/policies-v3`. **Baseline: 911 tests passing, zero warnings.**

---

## Five decisions settled before implementation

### 1. A context is prepared asynchronously, and an unprepared one is refused

Store loads are async; `ToList(filter)` is not. `IDwPolicyProvider.GetFragments` is synchronous and
documented not to perform I/O, so a user-level rule cannot be fetched at the moment it is needed.
Design §3.6 already answered this: build the context once per request through an async factory that
preloads the narrow zone.

What §3.6 does not say is what happens to a context that skipped the factory, and that is the whole
security question. Serving it from the broad zone alone means a user-level `Deny` silently does not
apply — the branch's recurring shape, one layer further out than Phase 4 found it.

`DwPolicyContext` therefore carries an internal attachment per store provider: the **pinned snapshot
reference** and **this caller's narrow-zone rules**. `StorePolicyProvider.GetFragments` throws
`PolicyContextNotPrepared` when the context has no attachment for it.

Refused unconditionally, not only when the caller holds a `User` subject. A carve-out reading "this
caller has nothing in the narrow zone, so the broad zone is complete" is correct today and is
exactly the kind of naming-axis reasoning that has failed open four times on this branch. One rule,
no exceptions, and the atomicity guarantee of §5.3 — a query that begins on version 41 finishes on
version 41 — comes free with it, because the snapshot is pinned per context rather than re-read per
field.

Preparation is idempotent and re-pins. A context must be fully prepared before its first query;
that is what "built once per request" means.

### 2. Every enum is rejected at the store boundary, not just the level

The roadmap carries one instance of this from Phase 1: `PolicyLevel` has no member at `0`, so a
level defaulting to `0` outranks `SealedAttribute = 1`. Reviewing it against the code found the same
trap on two more axes, and the combination is worse than any of them alone:

| Axis | Default | What an absent or unparsed column means |
|---|---|---|
| `PolicyLevel` | `0` | Outranks a sealed attribute |
| `PolicyEffect` | `Allow = 0` | A grant |
| `DwSubjectKind` | `Global = 0` | Applies to every caller |
| `PolicyFeature` | `None = 0` | `Covers()` is false for every feature, so the rule is inert |

**A rule row of all zeroes is "grant everyone everything, above sealed".** The roadmap names only
the first row. All four are rejected at the boundary: `PolicyRule`'s constructor refuses an
undefined `PolicyEffect`, an undefined `DwSubjectKind`, a `PolicyFeature` carrying an unknown bit or
equal to `None`, and never accepts a `PolicyLevel` at all — the level is *derived* from
`SubjectKind`, through an exhaustive mapping with no default arm, so `0` is unreachable by
construction rather than by a check someone can delete.

`PolicyFeature` cannot use `Enum.IsDefined`, being flags: `Where | Select` is `3` and defined
nowhere. The check is a mask — `(features & ~PolicyFeature.All) != 0` — plus a refusal of `None`.

Subject → level, exhaustive: `Global → DynamicGlobal`, `Tenant → DynamicTenant`,
`Role → DynamicRole`, `User → DynamicUser`, `Custom → DynamicTenant` (per `DwSubjectKind`'s own
documentation, which resolves a caller-defined dimension at the tenant level).

### 3. Transform detail is typed at the boundary, parsed once, never on the query path

Design §5.1 gives a rule `Payload string? (JSON)`. Phase 4 moved deliberately the other way, and
`PolicyFragment.AllowedOperators` records why: reading a restriction back out of an `object` means
an unchecked cast, and a failed cast yields null — which there means "no restriction". The same
failure on a mask payload means the field ships **unmasked**.

JSON makes that more likely, not less. So `PolicyRule` carries a typed `TransformStage?`, and one
shared `PolicyPayload` parser converts a persisted JSON column into stages. A parse failure rejects
the rule; it can never produce a null stage. Phase 6's Redis and EF stores map through that one
routine rather than inventing two, following the precedent `PolicyFragment.NormalizeAlias` sets —
one validator, whether the value came from an attribute or from a rule.

**§5.1's `Effect` column is out of date.** It lists `Mutate | Default | Generalize` as effects;
`PolicyEffect` has only `Allow`, `Mask` and `Deny`, because Phase 4 settled transforms as typed
stages that carry `PolicyEffect.Mask` on `Select`. A rule transforms by supplying a stage, not by
naming an effect. The documentation is corrected in Phase 9, not worked around here.

### 4. `FailClosed` throws

§5.5 says an exceeded `MaxSnapshotAge` escalates to FailClosed and does not say what that does to a
field. The tempting answer — emit a wildcard `Deny` for everything — **is not fail-closed**:
`[DwOperators]` emits `PolicyEffect.Allow` at `SealedAttribute` on `Where`, which outranks a
`DynamicGlobal` denial under the resolver's level ordering. Every field carrying an operator
restriction would stay filterable in precisely the state the mode exists to refuse.

So the provider throws `StoreUnavailable`, and every guarded query fails until a refresh succeeds.
Nothing outranks an exception. Unguarded queries and types no policy speaks to are unaffected,
because the provider is never consulted for them.

The same rule makes the startup guarantee structural rather than remembered: `StorePolicyProvider`
is built through an async factory that performs the first load, so **a startup load failure throws
before there is a provider to hand to `Configure`**. There is no window in which the process is
serving with an unknown policy state.

`MaxSnapshotAge` defaults to 15 minutes and refuses zero or a negative value. There is deliberately
no way to disable the ceiling; that is the entire argument of §5.5.

### 5. Validity is evaluated per query; enablement is baked at load

`Enabled` changes only through a write, and a write bumps the version, so filtering it at load is
correct. `ValidFrom` and `ValidTo` are different: baking them into a snapshot means a grant that
expired at noon keeps applying until the next successful refresh — under `LastKnownGood` with a dead
store, for the full `MaxSnapshotAge` window. That is the spec's own argument about expiry that
depends on something being remembered, one level down.

They are therefore compared against the clock at resolution, per query. The cost is one comparison
per candidate rule, on rules already filtered to the caller's subjects.

The clock is injectable and internal. A staleness ceiling that can only be tested by waiting fifteen
real minutes is a ceiling with no test.

---

## Deliberately not in this phase

- **Redis and EF Core stores.** Phase 6. This phase ships the contract, the conformance suite that
  binds them, and one implementation to prove both.
- **The admin surface, `/explain` and `/schema`.** Phase 7. Nothing here is reachable over HTTP.
- **Short-name entity lookup.** A rule names its entity by `FullName`. See below.
- **`AllowAggregate` and `MinGroupSize`.** Phase 8, unchanged by anything here.

---

## Two decisions worth recording, because both could have failed open quietly

**A rule names its entity type by `FullName`, and a name without a namespace is refused.** Matching
a stored string to a `Type` is a naming axis, and this branch has lost to four of those already.
`Name` alone collides across namespaces, so a rule meant for `Sales.Employee` would also apply to
`HR.Employee` — over-application, which is safe for a `Deny` and a silent grant for an `Allow`.
Matching only `FullName` fixes that but turns a short name into a rule that silently never matches,
which is a `Deny` that does nothing. So the constructor refuses a type name carrying no separator,
converting a silent miss into a rejection at the boundary. Phase 7's `/schema` is where an operator
gets to type a friendly name.

**Upsert cannot always check sealed fields, and resolution is what actually enforces them.** §5.7
says an operator cannot even attempt to grant `NationalId`, enforced at configuration time and at
resolution time. Configuration time needs a `Type`, and a store holds a string. `InMemoryPolicyStore`
therefore takes an optional type resolver: supplied, `UpsertAsync` refuses a rule targeting a field
a sealed attribute already speaks to; absent, it accepts, and the rule is inert because a sealed
attribute outranks every dynamic level. The conformance suite proves the resolution-time guarantee
independently of the configuration-time one, so the weaker half can never be mistaken for the whole.

---

## Order of operations

```
startup
  1. store.LoadAsync()          throws on failure, always          [NEW]
  2. StorePolicyProvider.CreateAsync(...)                          [NEW]
  3. DwPolicy.Configure(options, storeProvider)                    (unchanged)

per request
  4. DwPolicy.PrepareAsync(ctx) pin snapshot, preload narrow zone  [NEW]

per query
  5. SANITIZE / PIPELINE / TRANSFORM                               (Phases 2-4, unchanged)
       └─ resolver sweeps providers
            ├─ AttributePolicyProvider   (unchanged)
            └─ StorePolicyProvider       reads the pinned snapshot [NEW]

background
  6. watch or poll → LoadAsync → atomic swap                       [NEW]
```

Step 5 performs no I/O and takes no lock: it reads a reference the context pinned at step 4.

---

## File structure

**Create in `DynamicWhere.ex/Policies/`:**

| File | Responsibility |
|---|---|
| `Enums/StoreFailureMode.cs` | `LastKnownGood`, `FailClosed`, `StaticOnly` |
| `Storage/PolicyRule.cs` | The rule, and every boundary refusal in decision 2 |
| `Storage/StoreSnapshot.cs` | Immutable broad zone, version, load time, indexed by entity |
| `Storage/NarrowZone.cs` | One caller's preloaded user rules |
| `Storage/IDwPolicyStore.cs` | `IDwPolicyStore`, `IDwPolicyWritableStore`, `IDwPolicyRefresher` |
| `Storage/InMemoryPolicyStore.cs` | The reference implementation, writable |
| `Storage/PolicyPayload.cs` | The one JSON ↔ `TransformStage` routine |
| `Context/PolicyAttachment.cs` | Internal: pinned snapshot plus narrow zone |
| `Resolution/StorePolicyProvider.cs` | Rules → fragments, failure modes, refresh loop |

**Modify:** `DwPolicyContext` (the attachment), `DwPolicyOptions` (`StoreFailure`,
`MaxSnapshotAge`, `RefreshInterval`), `DwPolicy` (`PrepareAsync`), `PolicyErrorCode` (append 17–18).

**Create in `DynamicWhere.Tests/Policies/`:** `PolicyRuleTests.cs`, `PolicyPayloadTests.cs`,
`StoreProviderTests.cs`, `StoreFailureTests.cs`, `PolicyStoreConformanceTests.cs` — the last as an
abstract base with an InMemory subclass, so Phase 6 inherits the suite by deriving from it.

---

## Tasks

- [x] **1. `StoreFailureMode` and the options** — `StoreFailure`, `MaxSnapshotAge` defaulting to 15
      minutes and refusing zero or negative, `RefreshInterval` defaulting to 30 seconds.
- [x] **2. `PolicyRule`** — every refusal from decision 2, the wildcard refusals the memo carries
      (no alias, no requirement, no transform on `*`), the `FullName` rule, and the exhaustive
      subject → level mapping with no default arm.
- [x] **3. `PolicyPayload`** — JSON to typed stage for all six kinds, round-tripped, a parse failure
      rejecting rather than returning null.
- [x] **4. `StoreSnapshot` and `NarrowZone`** — immutable, version, load time, broad rules indexed
      by entity `FullName`, `Enabled` filtered at load.
- [x] **5. The store contracts** — `IDwPolicyStore`, `IDwPolicyWritableStore`, `IDwPolicyRefresher`.
- [x] **6. `InMemoryPolicyStore`** — load, version, watch, upsert, delete, version bumped on every
      write, optional type resolver for the sealed-field refusal.
- [x] **7. `PolicyAttachment` and `DwPolicyContext`** — per-provider attachment, prepared once.
- [x] **8. `StorePolicyProvider`** — subject matching case-insensitively as `DwSubject` does,
      validity per query, rules to fragments, unprepared context refused.
- [x] **9. `CreateAsync` and `DwPolicy.PrepareAsync`** — startup load failure throws before a
      provider exists.
- [x] **10. Failure modes** — `LastKnownGood`, `StaticOnly`, and the `MaxSnapshotAge` escalation
      throwing `StoreUnavailable`, against the injectable clock.
- [x] **11. Refresh** — watch when the store offers one, poll otherwise, atomic swap, manual
      `RefreshAsync`, and a refresh failure that keeps the last known good snapshot.
- [x] **12. The conformance suite** — the eight items of design §8.4, as an abstract base.
- [x] **13. Replace the Phase 1 fake provider** in the precedence tests that can use a real store,
      proving the store emits fragments the existing matrix already covers.
- [x] **14. End to end** — a store rule masking a field on the SQLite fixture, and the empty-store
      case from §8.5 where attributes still enforce.
- [x] **15. Phase gate** — release build zero warnings, full suite green, the four named files
      untouched, roadmap updated.

---

## What to watch for

- **A rule that fails to match is access granted.** Every match in this phase is a naming axis —
  entity type, field path, subject identity — and this branch has lost to four of them. Field paths
  go through `PolicyFragment.NormalizePath`, subjects compare exactly as `DwSubject.Equals` does,
  entity types match `FullName`. None of the three gets its own second spelling.
- **The provider must not cache per type.** Aliases and transform chains are per-caller, which is
  why `ResolveType` takes a context. Indexing the *snapshot* by entity type is fine and is not that;
  memoizing a resolved `TypePolicy` is the trap, from the other direction to Phase 4's.
- **A test that passes either way proves nothing.** Mutation-check the boundary refusals: construct
  the all-zero rule, delete each guard in turn, and confirm the suite goes red for each. Four
  separate guards means four separate mutation checks, not one.
- **`StorePolicyProvider` owns a timer.** It is `IDisposable`, and a suite that configures it 900
  times over must dispose it, or the failure surfaces as an unrelated flake much later.
