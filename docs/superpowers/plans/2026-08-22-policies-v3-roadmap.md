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
