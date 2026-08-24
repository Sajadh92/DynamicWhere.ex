# DynamicWhere.ex Policies v3.0 — Phase Roadmap

**Branch:** `feat/policies-v3`
**Spec:** [2026-08-22-dynamicwhere-policies-design.md](../specs/2026-08-22-dynamicwhere-policies-design.md)
**Goal:** Ship the field-level policy layer as v3.0 in a single release.

This is the roadmap, not a task plan. Each phase gets its own plan document written immediately
before it is executed, so that later phases are planned against the code that actually exists
rather than against a guess made weeks earlier.

---

## Release mechanics — read before starting

`.github/workflows/publish.yml` packs and pushes to NuGet on **every push to master** via Trusted
Publishing. The same push triggers Cloudflare Pages to rebuild doc.dynamicwhere.com. Neither has a
manual gate. A published NuGet version cannot be overwritten or deleted, only unlisted.

Therefore:

- All nine phases land on `feat/policies-v3`.
- Documentation, README, website, and the version bump are **Phase 9, on the branch** — not a
  follow-up after merge.
- Merging to master is the release. It is a one-way door.
- `build/check-version.ps1` must pass before merge. It fails when `<Version>` in
  `DynamicWhere.ex/DynamicWhere.ex.csproj` disagrees with the version strings in `README.md`,
  `DynamicWhere.ex/DOC.md`, `OfficialWebsite/lib/nav.ts`, `OfficialWebsite/package.json`, and
  `OfficialWebsite/app/docs/installation/page.tsx`.

---

## Decisions carried in from design

| Item | Resolved |
|---|---|
| Entry method name | `query.ApplyPolicy(ctx)` |
| Class-level guard | `[DwEntity(RequirePolicy = true)]` |
| Tokenize mask strategy | Deferred to v3.1 — 8 strategies ship in v3.0 |
| `Segment` policy feature | Gets its own `PolicyFeature` flag, not composed from Where + Select |
| `MinGroupSize` | Global option with a per-field attribute override |

---

## Phases

Each phase produces working, testable software on its own and ends green.

### Phase 1 — Foundation

Context, resolved policy model, options and tiers, attribute provider, the precedence resolver,
trace and exception types, startup validation.

No enforcement and no query execution. The provider abstraction ships with a test-only fake
provider so the **full six-level precedence matrix is exhaustively tested here**, before any real
store exists. The riskiest logic in the whole feature gets proven first, and Phase 5 then only has
to prove that a real store emits correct fragments.

**Exit:** given a decorated type and a context, the resolver returns the correct `FieldPolicy` for
every combination in the precedence grid. No database involved.

### Phase 2 — Gating

`ApplyPolicy(ctx)` handle, `FilterSanitizer` steps 1–8 and 10, `AsNoTracking`, the deny attributes
(`DwDeny` plus six sugar subclasses), `DwOperators`, caps, `[DwEntity(RequirePolicy)]`.

**Exit:** end-to-end guarded queries against the SQLite fixture. Blocked WHERE throws, blocked
ORDER and SELECT drop or throw per tier, caps reject, unguarded calls on a guarded entity throw.

**Decide `PolicyException.Code`'s type here, before anything throws one.** `ErrorCode` is an
`internal static class`, but `PolicyException.Code` is a public `string`. A consumer writing
`catch (PolicyException ex) when (ex.Code == "FieldDeniedForWhere")` must hardcode a literal the
library can rename with no compile error and no test failure anywhere downstream — the exception is
public, its data is public, and its vocabulary is not.

Do **not** fix this by making `ErrorCode` public: it holds around thirty members, most of them
internal validation strings unrelated to policy, and publishing all of them to expose nine commits
the rest as API surface permanently. Add a `PolicyErrorCode` enum on the exception alongside the
string instead — the policy codes are a closed set, callers get exhaustive switching with compiler
help, and the string survives for logging and serialization.

This phase is where library code first throws a `PolicyException`, so it is where the catch-site
ergonomics become visible. It is also the last moment the choice is free: once v3.0 ships, the
strings are de-facto API.

### Phase 3 — Injection

`FilterSanitizer` step 9. `[DwForceWhere]`, `[DwRequireWhere]`, `[DwAlias]`.

The wrap-not-merge correctness work from spec §4.3. Carries the pinned shape test that fails if
injection is ever rewritten as a merge.

**Exit:** injected predicate appears in generated SQL, wrapping shape pinned, missing
`ContextValue` throws in both tiers, aliases resolve bidirectionally.

### Phase 4 — Transformation

Mask engine with 8 strategies, compiled mutator cache, graph walker with cycle guard, `DwMutate`,
`DwDefault`, `DwGeneralize`, `DwTruncate`, `DwFormat`.

**Exit:** masking works on typed and dynamic projections, across direct, nested-reference, and
collection paths. Includes the SQL Server tracking-corruption test from spec §8.3 — the blocking
CI test proving a mask can never reach `SaveChanges()`.

### Phase 5 — Dynamic store

`IDwPolicyStore`, `IDwPolicyWritableStore`, `InMemoryPolicyStore`, snapshot with broad and narrow
zones, versioning and atomic swap, `StorePolicyProvider`, failure modes and `MaxSnapshotAge`.

The fake provider from Phase 1 is replaced by the real store provider against the same resolver.

**Exit:** store conformance suite passes against InMemory. Startup load failure throws, refresh
failure holds last-known-good, staleness ceiling escalates to FailClosed.

**Carried in from Phase 1 review — must be handled here.** `PolicyLevel` has no member with the
value `0`, so `default(PolicyLevel)` is `0` and sorts as *more* authoritative than
`SealedAttribute = 1` under the resolver's `Min()`. Phase 1 cannot reach that state because
`PolicyFragment` takes its level as a required positional argument. This phase can: a level read
from a database column or a JSON payload defaults to `0` when absent or unparsed, which would
silently outrank a sealed attribute. Reject an unmapped level at the store boundary rather than
letting it reach the resolver.

### Phase 6 — Store providers

`DynamicWhere.ex.Policies.Redis` and `DynamicWhere.ex.Policies.EntityFrameworkCore`, including the
`DwPolicyRules` and `DwPolicyVersion` schema and migrations.

**Exit:** the same conformance suite passes against all three implementations. Redis pub/sub
invalidation and DB version polling both verified.

### Phase 7 — Platform

`DynamicWhere.ex.Policies.AspNetCore`: admin API, explain, simulate, health, the
`ClaimsPrincipal` adapter. Plus `/schema` discovery with `[DwDescribe]` and `[DwAllowedValues]`,
`[DwAudit]` with `IDwAuditSink`, and `[DwCost]`.

**Exit:** explain returns the decision chain with sources and overrides; sealed fields absent from
`/schema` and rejected by `POST /rules`; simulate returns a sanitized filter without executing.

**Carried in from Phase 1 Task 11.** When two fragments tie on all four passes — level, specificity,
priority, *and* effect — the resolver's `contenders.First(...)` picks by provider order. The decided
**effect is fully deterministic**; only which of several equivalent `PolicySource` values gets
attributed is arbitrary. That has no security consequence, but the explain endpoint would name one
rule when another, identical in force, contributed equally. Either report every tied source rather
than the winner alone, or state in the explain output that the attribution is one of several.

### Phase 8 — Security hardening

Cross-cutting mitigations that need Segment and Summary policy support to already exist:
`Segment` per-subquery enforcement and the strict-tier deny-where rule, aggregate-on-masked denial,
`MinGroupSize`, `getQueryString` gating.

Ends with `PolicyInferenceTests` — seven tests that each reproduce an attack and assert the
mitigation blocks it, written so removing a mitigation turns the test red.

**Exit:** all seven inference attacks blocked. Earlier phases carry their own security tests
inline; this phase covers only what spans features.

### Phase 9 — Release preparation

Version bump across the six tracked files, README, `DOC.md`, website pages, release notes in the
csproj, EF Core 6.0.22 CI leg, full suite green, benchmarks within the 5 percent budget.

**Exit:** `pwsh ./build/check-version.ps1` passes, CI green on both framework legs, and the branch
is ready to merge. Merge is the release.

---

## Dependency order

```
1 Foundation
  └─ 2 Gating
       ├─ 3 Injection
       └─ 4 Transformation
            └─ 5 Dynamic store
                 ├─ 6 Store providers
                 └─ 7 Platform
                      └─ 8 Security hardening
                           └─ 9 Release preparation
```

Phases 3 and 4 are independent of each other and can be worked in either order. Phases 6 and 7 are
likewise independent once Phase 5 lands.

---

## Resolved after Phase 1 — jagged collections are walked

This was recorded as a known limitation and has since been fixed in `020ab4f`. Investigating it
found the defect was wider than reported: an array type's `Namespace` is its *element's* namespace,
so `AsNavigation` was accepting the array itself and `Post[][]` resolved to `Post[]`, sending the
walker into `Length`, `Rank`, and `SyncRoot`. `PagedList<LineDto>[]` and `List<List<Post>>` each
failed differently again.

`NavigationTypeOf` now peels collection layers in one bounded loop and applies the scalar checks
once at the end, so arrays and collections cannot drift apart. The bound is a layer count, not a
`next == current` check — the latter loops forever on a two-step cycle
(`A : IEnumerable<B>`, `B : IEnumerable<A>`), and unbounded recursion on `class Weird :
IEnumerable<Weird>` was confirmed to kill the process with a stack overflow rather than throw.
On exhausting the bound the walker descends into the type it reached rather than returning null,
because returning null would skip the subtree and fail open again.

Phase 9 owes the public reference nothing here. Six navigation gaps of this shape were found during
and after Phase 1; all six are closed.

## Carried forward from Phase 1

**If `AttributePolicyProvider` ever gains configuration, fix its cache key first.** The provider
caches fragments in a `static ConcurrentDictionary<Type, IReadOnlyList<PolicyFragment>>` shared
across every instance. That is sound only while the mapping from attribute to fragment depends on
nothing but the type — which is true today, since `GetFragments` ignores its `context` argument
entirely. The moment options are introduced (anything changing how attributes map to levels, or
which attributes are read), two differently-configured instances would silently share whichever
result was computed first. In a security component that is a wrong answer, not a stale one, and it
would not surface as a test failure in the task that introduced the options. Either include the
options in the cache key or make the cache per-instance, in the same change that adds them.

Secondary, lower priority: `Type` keys held in a process-lifetime static pin their assemblies, so a
consumer using a collectible `AssemblyLoadContext` for plugins or runtime codegen cannot unload it.
Irrelevant for a fixed entity set; real for a plugin host.

## Standing rules for every phase

- TDD. The failing test comes first, and it is run and seen to fail before implementation.
- Commit at the end of every task, not at the end of a phase.
- No edits to `Builder.cs`, `Validator.cs`, `Converter.cs`, or `Normalizer.cs`. The design is a
  sandwich around the existing pipeline; a diff touching those files means something went wrong.
- Existing tests stay green at every commit. The unguarded path must remain byte-identical in
  behaviour to v2.1.5.
- Nothing is pushed to master until Phase 9 is complete and verified.
- **One implementing agent at a time per worktree.** Disjoint files are not sufficient isolation:
  the git index is shared process-wide, so a tree-wide `git add` in one agent stages and commits
  another agent's in-flight work. This happened during Phase 1 Task 6 and required splitting a
  mixed commit back apart. Either serialize implementers, or give each one its own worktree.
- **Stage with explicit pathspecs.** Never `git add -A`, `git add .`, or `git add -u`. Name the
  files. This holds even when serialized, because the plan documents are routinely modified in the
  working tree between tasks and must not be swept into a code commit.

## Phase 1 outcome

Closed 2026-08-24, 18 tasks. 509 tests pass. `dotnet build DynamicWhere.ex -c Release` emits zero
warnings with `GenerateDocumentationFile` on, and `git diff master...HEAD -- DynamicWhere.ex/Source/`
is empty: nothing in the existing pipeline was touched.

### Public API added

- `DynamicWhere.ex.Policies.Enums` — `PolicyFeature`, `PolicyEffect`, `PolicyLevel`, `DwTier`,
  `DwSubjectKind`
- `DynamicWhere.ex.Policies.Context` — `DwPolicyContext`, `DwSubject`
- `DynamicWhere.ex.Policies.DTOs` — `PolicyFragment`, `FieldPolicy`, `PolicySource`
- `DynamicWhere.ex.Policies.Attributes` — `DwPolicyAttribute`, `DwDenyAttribute`,
  `DwEntityAttribute`, and six sugar attributes: `DwDenied`, `DwNoWhere`, `DwNoSelect`, `DwNoOrder`,
  `DwNoGroup`, `DwNoAggregate`
- `DynamicWhere.ex.Policies.Config` — `DwPolicyOptions`, `DwCaps`
- `DynamicWhere.ex.Policies.Resolution` — `IDwPolicyProvider`, `AttributePolicyProvider`,
  `PolicyResolver`
- `DynamicWhere.ex.Exceptions` — `PolicyException`

### Five fail-open defects found and fixed

1. Subject identity compared case-sensitively while field paths did not.
2. Field paths trimmed only at the ends.
3. Nested paths not normalized per segment, so `Customer. Name` missed a deny aimed at
   `Customer.Name`.
4. Array, interface, and struct navigations not descended.
5. Custom collection types not unwrapped.

All five are the same defect in five places: a lookup key and a stored key normalized differently,
in a system where a fragment that fails to match means access granted. None of the five was caught
by a test. All five were caught by review.

### Known limitations

None outstanding. Jagged collections were the one open item and were fixed in `020ab4f`; see
"Resolved after Phase 1" above.

### What Phase 2 inherits

- **The `PolicyErrorCode` decision.** Settle `PolicyException.Code`'s type before library code
  first throws one — that happens in Phase 2, and after v3.0 ships the strings are de-facto API.
- **The provider cache-key gate.** `AttributePolicyProvider`'s static cache is keyed on `Type`
  alone; it must be re-keyed or made per-instance in the same change that gives the provider any
  configuration.
- **The `default(PolicyLevel)` gate.** `PolicyLevel` has no member with value `0`, so an unmapped
  level outranks a sealed attribute. Reject it at the store boundary in Phase 5.
- **The explain-attribution note.** A four-way tie decides the effect deterministically but
  attributes one arbitrary source of several. Due with the explain endpoint in Phase 7.
