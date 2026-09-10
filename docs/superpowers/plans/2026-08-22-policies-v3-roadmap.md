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

Mask engine with 8 strategies, compiled mutator cache, graph walker, `DwMutate`, `DwDefault`,
`DwGeneralize`, `DwTruncate`, `DwFormat`. **Done — see the Phase 4 outcome below.** The walk turned
out to need no cycle guard: it is driven by the policy's finite paths rather than by the object
graph, and shared objects are handled by reference identity.

**Exit:** masking works on typed and dynamic projections, across direct, nested-reference, and
collection paths. Includes the SQL Server tracking-corruption test from spec §8.3 — the blocking
CI test proving a mask can never reach `SaveChanges()`.

### Phase 5 — Dynamic store

`IDwPolicyStore`, `IDwPolicyWritableStore`, `InMemoryPolicyStore`, snapshot with broad and narrow
zones, versioning and atomic swap, `StorePolicyProvider`, failure modes and `MaxSnapshotAge`.
**Done — see the Phase 5 outcome below.** The store contract needed a fourth member: section 5.3
splits user rules out of the snapshot and section 5.2 gave them nowhere to be fetched from.

The fake provider from Phase 1 is not replaced, and cannot be: two of the six precedence levels are
compile-time attributes and no store can produce one. The dynamic half of the grid is re-run through
a real store instead.

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
`DwPolicyRules` and `DwPolicyVersion` schema and migrations. **Done — see the Phase 6 outcome
below.** The schema ships and the migrations do not: a migration is generated per provider, and
§5.6's claim that one package serves SQL Server, Postgres and anything else with an EF provider
stops being true the moment one is shipped.

**Exit:** the same conformance suite passes against all three implementations. Redis pub/sub
invalidation and DB version polling both verified.

### Phase 7 — Platform — **Done**

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

### Phase 8 — Security hardening — **Done**

Cross-cutting mitigations that need Segment and Summary policy support to already exist:
aggregate-on-masked denial and `MinGroupSize`.

**Two of the original five landed early, after Phase 3.** `Segment` per-subquery enforcement, the
strict-tier deny-where rule (section 7.1), and `getQueryString` gating (section 7.5) all became
reachable the moment injection landed, and none of them needed anything from the mask engine. They
shipped in `fe5e33b` rather than waiting. What remains here genuinely depends on masking existing:
"aggregating a masked field is denied" presupposes masks, and `MinGroupSize` without
`AllowAggregate` to bound is a hole with a lid on it rather than a mitigation.

`PolicyInferenceTests` already exists and holds the two closed channels. This phase completes it to
seven, each test reproducing an attack and asserting the mitigation blocks it, written so removing a
mitigation turns the test red.

**Exit:** all seven inference attacks blocked. Earlier phases carry their own security tests
inline; this phase covers only what spans features.

### Phase 9 — Release preparation — **Done**

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

## Phase 2 outcome

Closed 2026-08-26, 19 tasks. 663 tests pass, up from 516. `dotnet build -c Release` across the whole
solution emits zero warnings with `GenerateDocumentationFile` on.

The gate check was narrowed during review, from the whole `DynamicWhere.ex/Source/` directory to the
four files the standing rule actually names. `Extention.cs` lives in that directory and had to take
the `RequirePolicy` guard, so the directory form and Task 13 could not both hold. The four named
files remain untouched; `Extention.cs` gained 43 lines, all insertions, all of them the same
two-line guard plus one `using`.

### Public API added

- `DynamicWhere.ex.Policies.Enums` — `PolicyErrorCode`, `PolicyAction`
- `DynamicWhere.ex.Policies.DTOs` — `PolicyDecision`, `PolicyTrace`
- `DynamicWhere.ex.Policies.Attributes` — `DwOperatorsAttribute`
- `DynamicWhere.ex.Policies.Config` — `DwPolicy`
- `DynamicWhere.ex.Policies.Source` — `PolicyQueryable<T>`, `PolicyExtensions.ApplyPolicy`
- `PolicyException.ErrorCode`; `FieldPolicy.AllowedOperators` and `AllowsOperator`;
  `PolicyFragment.AllowedOperators`; `FilterResult<T>.Policy` and `SummaryResult.Policy`
- `InternalsVisibleTo("DynamicWhere.Tests")`, so the sanitizer, the deep clones, and the guard stay
  internal rather than being published to be testable

### Three defects found in review, before any code was written

Same shape as Phase 1's five: a fragment that fails to match resolves to Allow, so every naming axis
is a place access can be granted silently. None would have been caught by the tests as planned.

1. **Deny-select did nothing unless the caller volunteered a projection.** `Extension.ToList`
   projects only `if (filter.Selects != null)`, so gating that list enforced the policy against
   exactly the callers who asked for less. Closed by synthesizing a projection from the allowed
   fields when something is denied — and only then, so a type nothing is denied on still produces a
   null projection and byte-identical SQL.
2. **`Summary.Having` reaches fields through aggregate aliases.** `Validate<T>()` throws on an
   alias, so canonicalizing one would have rejected queries that work today; skipping it would have
   let `HAVING SUM(Salary) > n` ask what the where clause was refused. Gated through the field each
   alias aggregates.
3. **`Summary.Orders` accepts a third spelling nobody had accounted for.** Beyond property paths and
   aliases, the validator also accepts a group-by key with its dots stripped — `Contact.Phone`
   becomes `ContactPhone`, because that is the alias the projection emits. Found while implementing
   the fix for (2). Every reference name now maps to a *list* of paths and all of them are gated,
   so colliding names fail closed.

Phase 1 also left nine policy code strings on the internal `ErrorCode` class. Nothing referenced
them and one had already drifted from its own member name — `PolicyRequired` returned
`"PolicyContextRequiredForThisEntity"`. Removed; `ErrorCode.cs` is identical to master again.

### Decisions worth not re-litigating

- **`PolicyFragment.Payload` is not the carrier for operator restrictions.** It is `object?`, so
  reading one back needs an unchecked cast whose failure yields null — meaning "no restriction".
  More decisively, a restriction is not an effect: the resolver elects one winner per feature and
  discards the losers, so a restriction on a losing fragment would vanish. Restrictions ride on a
  typed field and are **intersected** outside the election, which can only narrow.
- **The policy scope is ambient.** The guarded path delegates to the very extension methods being
  guarded, and those call one another internally, so a checked/unchecked split would have had to
  reach all the way down. `AsyncLocal`, not `ThreadStatic`, because a scope must survive an `await`.
- **`DwPolicy` defaults to enforcing, not to inertness.** A policy layer that does nothing until
  someone switches it on is worse than none, because the attributes in the source read as though
  they are already in force. `AttributePolicyProvider` is always in the resolver whether listed or
  not — attributes are the sealed level, and omitting them would let a store grant what the code
  refuses.
- **Projection synthesis never throws in the strict tier.** Strict refuses what a caller *asks* for;
  a caller who named no projection has asked for nothing to refuse, and throwing would fail every
  strict query against a type carrying any denied field.

### Known limitations

- **Deny-select on a nested field is not enforced through an eagerly loaded navigation.** Synthesis
  covers scalars only. An unguarded call does not populate navigations anyway, so the ordinary path
  is closed; a caller who `Include`s one and holds a type with denials has that navigation dropped
  from the synthesized projection, which fails closed rather than open. Masking on the materialized
  graph is the real answer and belongs to Phase 4.
- **`Segment` gets participation gating only.** A field denied for `Segment` is refused anywhere
  inside one, and each condition set is gated independently. The strict-tier rule making a
  deny-select field automatically deny-where inside a `Segment` is Phase 8, as planned.

## Phase 3 outcome

Closed 2026-08-27, 11 commits. 805 tests pass, up from 663. `dotnet build -c Release` across the
whole solution emits zero warnings with `GenerateDocumentationFile` on, and the four named files are
untouched — `git diff master...HEAD` over `DynamicWhere.ex/Source/` shows only the 43 lines
`Extention.cs` took in Phase 2.

Plan: [2026-08-27-policies-v3-phase-3-injection.md](2026-08-27-policies-v3-phase-3-injection.md).

### Four forks settled before implementation

1. **`[DwRequireWhere]` is satisfied only by a conjunctively binding condition** — one whose path
   from the root is unbroken `And`, using an operator from the satisfying set (`Equal`, `IEqual`,
   `In`, `IIn` by default). `Status = A OR TenantId = 5` names the field and still returns every
   other tenant's rows matching A.
2. **`[DwAlias]` adds a spelling and never removes one.** The internal path keeps working, so
   aliasing a field is not a breaking change. Hiding the internal name is Phase 7's `/schema` job.
3. **A refusal reports the spelling the caller used.** Returning the canonical path for a field the
   caller only ever named by alias turns every refusal into schema disclosure.
4. **Runtime rules may set an alias, not only attributes** — chosen deliberately so a caller with
   the entitlement can rename fields to match their own understanding of the data.

The fourth answer is the consequential one. It makes the public field vocabulary **per-caller**, so
`TypePolicy` resolves against a context and cannot be cached per type. It also makes collision
security-relevant rather than cosmetic: a rule aliasing `"Salary"` onto another field, on a type that
already has a `Salary` property, gives one name two paths. Neither reading is safe to guess, so an
ambiguous name is refused outright with a new `AmbiguousFieldName` code.

### Public API added

- `DynamicWhere.ex.Policies.Attributes` — `DwAliasAttribute`, `DwRequireWhereAttribute`,
  `DwForceWhereAttribute`
- `DynamicWhere.ex.Policies.DTOs` — `ForcedPredicate`, `TypePolicy`
- `DynamicWhere.ex.Policies.Enums` — `PolicyErrorCode.RequiredFilterMissing`, `.MissingContextValue`,
  `.AmbiguousFieldName`, appended at 11–13
- `PolicyFragment.Alias`, `.Forced`, `.RequiredOperators`
- `FieldPolicy.Alias`, `.ForcedPredicates`, `.RequiredOperators`, `.IsRequiredInWhere`,
  `.SatisfiesRequirement`
- `PolicyResolver.ResolveType`

Alias and required are **elected** — one per field per caller, by the ranking the effects use, so a
sealed attribute beats a rule. Forced predicates are **collected and ANDed**, like the operator
intersection: a conjunction can only narrow, and electing one would let a low-authority rule
silently discard a sealed tenant scope.

### Three fail-open defects found, none by a test

Ten now across three phases, all found by reading.

1. **Injection on the filter path alone left three doors open.** Spec section 6.2 lists step 9
   against the `Filter` pipeline and mentions no other surface. `Summary` would have let a caller
   read an aggregate across every tenant by asking for a grouped result; `Segment` scoped in one arm
   of an `Except` and not the other returns precisely the rows the scope hides; and the composable
   `Select`, `Order` and `Page` each return an `IQueryable` the caller materializes, so a scope
   applied only to the terminal methods is bypassed by composing instead of terminating. A segment
   carrying *no* condition sets needed one created, since the pipeline reads an empty set list as
   "return everything".
2. **An aliased group-by key reached `Having` and `Summary.Orders` unaliased.** The key is rewritten
   to its canonical path during resolution, and the caller then names the alias again in a clause
   `BuildReferences` never learned. The reference matches nothing, the gate skips it, and a field
   denied for `Order` is sorted by. The fourth spelling arriving exactly where the third one was
   missed.
3. **`PolicyQueryable.Where(Condition)` handed the pipeline `Conditions[0]`.** After injection that
   index is the library's own term and the caller's condition has moved into a subgroup, so the
   caller's filter would have been silently discarded — and a dropped condition widens the result.
   Found while wiring Task 8, not by a test.

### Two corrections made during execution

Both settled against the design document rather than against the plan.

- **Dry run wins over injection.** The draft had a missing `ContextValue` throwing in dry run. Spec
  section 2.5 says dry run overrides the whole table, and dry run's promise is the same data the
  unguarded path returns — a predicate that narrows the result would make a canary understate its
  own blast radius. Nothing is injected and nothing throws; both the injection and the unresolved
  value are recorded instead.
- **`[DwForceWhere]` accepts `IsNull` and `IsNotNull`.** They compare against nothing, so they set
  neither `Value` nor `ContextValue`. Soft deletion is usually spelled `DeletedAt IS NULL`, and
  without this the commonest forced predicate of all needed a sentinel date.

### Decisions worth not re-litigating

- **An alias or a requirement on the wildcard path is refused at construction, not ignored.** One
  name cannot stand for every field, and a demand to filter on every field refuses every query.
  Ignoring nonsense is how a misconfigured rule becomes invisible. A forced predicate on a wildcard
  fragment is fine, because the predicate names its own field.
- **`[DwForceWhere]` has no `DataType` override.** C# forbids a nullable enum as an attribute
  argument, and an override could only ever disagree with the type the pipeline validates against.
  It is read from the decorated member; a CLR type with no counterpart (`TimeSpan`, `TimeOnly`) is
  refused at the misconfiguration.
- **`RequireWhere` is checked on every surface, the composable ones included.** Deliberately not
  Phase 2's projection-synthesis reasoning: there, a caller who named no projection had asked for
  nothing to refuse; here, a caller who named no scope has asked for everything.
- **A group with a single child conjoins whatever its connector says.** The pipeline emits that
  child alone, so treating it as a disjunction would refuse a filter that does narrow the result and
  push callers into rewriting sound filters to please the checker.
- **Injected predicates spend neither the condition budget nor the depth budget.** The caps count
  what the caller sent. A filter sitting exactly on `MaxConditions` must not be refused because the
  library added a term.
- **`Resolve` and `ResolveType` share their election helpers**, with a test pinning them to the same
  answer. Two routines agreeing only by construction is the standing liability on this feature.

### Known limitations

- **Outbound alias renaming is not delivered.** "Bidirectional" is shipped here as inbound rewriting
  plus alias-faithful refusal and trace reporting. A typed `FilterResult<T>` cannot rename anything,
  and a dynamic projection's names are baked in by the `Select` projection string in `Extention.cs`,
  which the standing rule says takes nothing beyond its Phase 2 guard. `FieldPolicy.Alias` is public
  so Phase 7's `/schema` can advertise the caller's vocabulary.
- **An aliased type reached by two navigations makes that alias unusable on the root type.** Both
  paths are recorded and the name is refused as ambiguous. That is the honest consequence of walking
  navigations; the internal paths still work.
- **`ValidatePolicyModel()` still does not exist.** Section 4.8's rules are enforced at query time
  and fail closed. `[DwForceWhere(ContextValue = ...)]` naming a key nothing supplies is listed
  there as a startup check and cannot be one — what a context supplies is per-request.

## Phase 9 outcome

Release preparation, 2026-09-08. Everything below is on the branch; **nothing is merged**, and the
branch is open as a pull request for review rather than going straight to master.

**Gates, all green.** `check-version.ps1` passes at 3.0.0 · solution builds with **0 warnings, 0
errors** · **1441 tests** on EF Core 8 · **1040** on the 6.0.22 floor · docs site builds · the four
protected query files untouched.

### Public API added

- `AttributePolicyProvider` walks with a cycle guard; `FilterSanitizer` prefers the root path when
  one member is reached many ways. No new public types — this phase changed behaviour, not surface.

### Three carriers were replicating around a cycle, and one failed silently

The phase's headline finding, and it came from running the demo API against a real PostgreSQL
rather than from a test. A type reachable from itself — `Employee.Manager`, `Category.Parent` —
had one declaration turned into fifteen paths by the depth cap, and three attributes that describe
*the entity being queried* were replicated onto every one.

| Carrier | What it did | How it failed |
|---|---|---|
| `[DwAlias]` | One name matched fifteen paths | `AmbiguousFieldName` on every use — loud |
| `[DwRequireWhere]` | The demand reappeared as `Manager.Division` | `RequiredFilterMissing`, unsatisfiable — loud |
| `[DwForceWhere]` | `IsActive = true` ANDed with `Manager.Manager.Manager.IsActive` | **Returned zero rows — silent** |

All three fail closed, which is exactly why 1428 tests stayed green: two refuse and one returns
fewer rows. Any bidirectional navigation triggers it, which is most models.

The fix is a **cycle check, not a depth-0 check**: forcing `Buyer.TenantId` while querying `Order`
is a real thing to declare, and only a type reflected back onto itself is meaningless. Everything
that *decides* still propagates — a caller can name `Manager.Salary`, so the denial and the mask
have to be there. That is why the walk still has no visited-type guard, and why this is three
attributes rather than a change to how the walk terminates.

Same family as Phase 7's "a fragment that carries something must decide nothing", one layer up.
Phase 7 fixed carriers winning elections; nobody had asked whether the walk should be replicating
them at all.

### A mutation check found a gap, again

Four mutations against the carrier fix, then three against the cycle guard. The third — never
unwinding the guard — **left the suite green**. The diamond case had no test, and without one the
guard could suppress a carrier on a sibling branch and reintroduce the same empty result it was
added to remove. `A_type_seen_on_one_branch_still_carries_on_another` now covers it, and the
mutation goes red. Second phase running where a green mutation was the finding.

### The performance budget was never measured, and is not met

Design §6.5 claimed under 5 percent. BenchmarkDotNet, medium job, 10,000 in-memory rows:

| | Unguarded | Gating only | Gating + transforms |
|---|---|---|---|
| Time | 741 µs | 857 µs (1.16×) | 1,933 µs (**2.61×**) |
| Allocated | 210 KB | 409 KB (1.94×) | 3,397 KB (**16.2×**) |

The per-operation targets pass with room to spare — a cached resolve is **239–250 ns** against 1 µs,
sanitizing five conditions is **2.8 µs** against 50 µs. What the 5 percent missed is that those are
paid once per query while the transform walk is paid per row and clones what it touches. No database
round trip in these numbers, so the layer's share looks as large as it ever can. §6.5 is restated
rather than defended.

`DynamicWhere.Benchmarks` is **not wired into CI**, deliberately: a benchmark gate on a shared runner
fails for noise, and the workflow it would gate publishes to NuGet.

### The floor leg had never run

`ci.yml` had no EF Core 6.0.22 leg at all — the roadmap's wording implied a three-project leg
existed. Four packable projects declare 6.0.22 and nothing had ever *executed* there. It now runs
1040 tests on the floor and gates `publish.yml` as well as `ci.yml`, placed ahead of the release
build because it resolves a different package graph and `Pack` runs `--no-build`.

Dropping Npgsql, Testcontainers and Mvc.Testing on that leg is not tidiness: all three depend on EF
Core 8, and leaving them referenced lets NuGet resolve the graph back up to 8, at which point the leg
passes while proving nothing.

Running it found `TransformQueryTests` reading the stored value through `Database.SqlQuery<T>`, which
arrived after EF Core 6 — the three assertions proving a transform never reached the database. They
are now provider-neutral ADO and run on both legs rather than being skipped on the one that matters.

### Design §8.3 shipped whole

`9_PolicyTestController.cs` (16 endpoints) and `__PolicyAdminController.cs` (8), against a real
PostgreSQL, with `Employee` carrying the full attribute range and `RequirePolicy`. All 24 pass.

The blocking no-write-back test is `GET /api/PolicyTest/tracking/no-writeback`: 7 rows read, **0
tracked entities**, email masked on output, and the stored email and salary intact after an unrelated
`SaveChanges` on the same `DbContext`.

The controller asserts the **error code**, not just the throw. Half of what a policy does is refuse,
so the house catch-reports-failure shape would mark a working denial as broken and a removed control
as a pass — exactly inverted. That choice is what caught four wrong expectations of my own, each one
the library being right: a denied field in `Convenience` is **dropped** not refused (§2.5); the group
floor **suppresses** rather than refusing; the cost test tripped `MaxConditions` first.

### Eight spec corrections

§3.4 claims adapter · §5.1 four missing carriers · §5.2 `LoadNarrowAsync` · §5.7 sealed-per-feature ·
§7 five channels are seven mitigations · §7.2 every transform not masks alone · §8.3 PostgreSQL not
SQL Server · §6.5 the performance budget. Footnoted inline rather than edited into the prose.

### Documentation, from nothing

`README.md` and `DOC.md` contained **zero** occurrences of "policy" before this phase. Both now carry
it, plus nine site pages matching the depth the cache feature sets. Security has its own page because
`MinGroupSize` is the one control a reader cannot infer from the API.

### Known limitations carried into the release

- **`/schema` returns 335 fields for a 33-property self-referencing entity.** The depth-4 walk
  enumerates every `Manager.Subordinates.Manager.…` combination. Correct — those paths really are
  filterable — but unusable as a filter UI without collapsing cycles for presentation. The carrier
  fix did **not** change this, and an early note in this phase wrongly said it would.
- **Overhead is ~15 percent gating, ~2.6× transforming**, not 5 percent. Documented, not fixed.
- **`MinGroupSize` ships at 1 — off.** The compatible default, not the safe one.
- **Fragments are still rebuilt per field per query.** Now measured: it is not the dominant cost.
- **`MaskStrategy.Tokenize` deferred.**

### Four of those five closed, 2026-09-10

Everything above except the schema explosion, which is being designed separately.

- **The budget is two budgets, and both are met.** The single 5 percent figure contradicted the
  design's own 100 ns per row per value: ten thousand rows with two transformed fields is 2 ms of
  transform work against a 700 µs query. Gating now has a budget of its own and measures at
  **1.00× time, 1.05× allocations**; transformation is budgeted per value at **21 ns and 65 bytes**
  against a 100 ns target. The walker was rewritten around the fact that it is the only part of this
  layer whose cost grows with the result — root set built once per result rather than once per path,
  accessors resolved once per runtime type rather than once per row, and a `readonly struct` context
  in place of an allocation per value. The transforming case went from **2.61× to 1.62×** in time and
  **16.2× to 7.1×** in allocations. `GuardedNoPolicy` is the new benchmark that isolates gating; the
  old numbers could not, because the only gating measurement also denied a field and so paid for a
  synthesized projection.
- **`MinGroupSize` ships on, at 5.** The compatibility argument did not survive being looked at: the
  floor applies only to a guarded summary, and guarded queries are new in this release. The setting
  starts *unset* rather than at one, so `MinGroupSize = 1` still means "no floor" and is honoured in
  production with nothing refused — `IsMinGroupSizeSet` is what tells the two apart.
- **`MaskStrategy.Tokenize` ships.** A random token from `DwPolicyOptions.TokenVault`, scoped to the
  field path by default. Three vaults: `InMemoryTokenVault` in the core, `RedisTokenVault` and
  `EfTokenVault` in the providers, all held to one conformance suite.
- **`Hash` is HMAC-SHA256 and its salt has a floor.** The old `SHA256(salt || value)` collided
  whenever a pair could be re-split, and a salt is now refused below 16 characters. Neither strategy
  hides equality, which design §7.6 now states outright rather than leaving to be inferred.

Still open: **`/schema` returns 335 fields for a 33-property self-referencing entity.**

### What a merge does

`publish.yml` packs and pushes four packages to NuGet on every push to master, and the same push
rebuilds the docs site. Neither has a manual gate; a published version can be unlisted, never
replaced. The suite runs before the pack and needs a Docker daemon.

## Phase 8 outcome

Closed 2026-09-07, 7 commits. 1428 tests pass, up from 1375. `dotnet build -c Release` across the
whole solution emits zero warnings, and the four named files are untouched — as is the whole of
`Source/`.

Plan: [2026-09-07-policies-v3-phase-8-hardening.md](2026-09-07-policies-v3-phase-8-hardening.md).

### Four decisions settled before implementation

1. **The bar is seven mitigations across five channels.** §7.1 delivered two and §7.2 needs two,
   which is where the roadmap's seven and the spec's five reconcile.
2. **Every transform blocks aggregation, not only `[DwMask]`.**
3. **The floor injects the count it needs and strips it again.**
4. **A group under the floor is dropped, and the drop is recorded.**

All four taken as offered.

### The seven, and where each one lives

| # | Channel | Mitigation | Shipped |
|---|---|---|---|
| 1 | §7.1 | Policy applies to every segment independently | `fe5e33b` |
| 2 | §7.1 | Strict tier: deny-select ⇒ deny-where inside a `Segment` | `fe5e33b` |
| 3 | §7.5 | Strict tier refuses `getQueryString` | `fe5e33b` |
| 4 | §7.2 | Aggregating a transformed field is denied by default | Phase 8 |
| 5 | §7.2 | `MinGroupSize` suppresses a group too small to hide anyone | Phase 8 |
| 6 | §7.3 | `[DwOperators]` restricts the field; `TotalCount` is a documented consequence | Phase 1 |
| 7 | §7.4 | The startup scan warns on masked-but-orderable; `[DwNoOrder]` is the fix | Phase 4 |

Six and seven needed no code. What they lacked was a test that reproduces the attack and shows the
control stopping it — a mitigation nobody has attacked is a claim rather than a control. Both are
now mutation-checked: removing the operator intersection turns one red, removing the startup
warning turns one red.

### Public API added

- `TransformStage.AllowAggregate`, `.MinGroupSize`; `ValueTransform.AllowsAggregate`, `.MinGroupSize`
- `AllowAggregate` and `MinGroupSize` on all six transform attributes
- `DwCaps.MinGroupSize`; `PolicyErrorCode.GroupTooSmall`

### §7.2's wording is short by five attributes

The spec says "aggregating a masked field is denied by default". Aggregation runs in SQL against
the stored values, before any stage of a transform chain applies — which is just as true of
`[DwGeneralize]`, and `[DwGeneralize]` is what §4.4 recommends for numeric members, which is to say
exactly the fields anybody aggregates. Covering only `[DwMask]` would have left five attributes that
look protective and are not.

Enforced in `PolicyResolver`, where the chain is elected, rather than at each surface that
aggregates: one place covers attributes and runtime rules alike, and the standing lesson of Phases 3
and 4 is that a list of surfaces is never finished.

### One existing test was the attack, written as a feature

Phase 4 shipped `A_summary_transforms_its_aggregates`, asserting that `MAX` over a generalized
`Age` returns the real maximum rounded on the way out. That is §7.2 verbatim. The field now opts in
deliberately, with a comment saying why, so it keeps proving the half that matters — the transform
runs after the grouping, so the aggregate picks the right row — while `PolicyInferenceTests` owns
the un-opted case.

### Three defects found by reading, none by a test

Twenty-six of this shape now, across eight phases.

- **The reshaping pass returned early whenever a type had no aliases**, which is most types — so the
  group-size column the floor added stayed in the result of every summary that was floored. Found
  while wiring, before the tests ran.
- **`PolicyPayload` dropped both new stage fields.** It was written a phase before either existed
  and a rule may set a transform, so both were lost whenever a rule's transform went through a
  store. Losing the permission is fail-closed and merely wrong; losing the floor is fail-open — the
  smallest group an operator was willing to have a field aggregated over silently became no floor,
  while the rule they read back still said otherwise. Phase 6's finding exactly, one phase later.
- **The floor ran after the summary transform**, so two groups that both fall below it and whose
  keys collide once rounded refused the entire summary — denying a result because of rows the caller
  was never going to be shown. Suppression now runs first of the three passes.

### A mutation check found a gap rather than confirming one

Of the four mutations aimed at the floor, three turned tests red and one turned nothing red: keeping
a row whose size column could not be read. That fail-closed choice was documented in a remark and
asserted nowhere, which is the shape this branch has learned to distrust — a comment is not a test,
and a test that passes either way proves nothing. Four tests now drive the suppressor directly, and
the same mutation turns two red.

### Decisions worth not re-litigating

- **The floor is off by default and applies to every grouped summary once on.** A group of one is a
  re-identification risk whatever is in it, and making the floor conditional on a transform would
  leave an unmasked-but-sensitive field with none.
- **The injected count goes in after gating and after the cost pass**, exactly as a forced predicate
  does. It is the library counting on its own behalf, so it is neither checked against the caller's
  policy nor billed to their budget.
- **A caller who has taken the reserved alias is refused, not overwritten.** Overwriting loses
  whatever they were counting and hands back the library's number as theirs.
- **A row whose size column cannot be read is dropped.** The column is one this library added, so
  failing to find it means the floor has no idea how large the group is — and answering anyway is
  the disclosure the floor exists to prevent.
- **An absent `allowAggregate` or `minGroupSize` in a payload reads as the strictest meaning**, so a
  document written before they existed permits no aggregation and sets no floor of its own.
- **§7.3 is a documented consequence and is now on the record as one.** A test asserts that a range
  filter on an unrestricted field really does count the matches while the column comes back zeroed,
  counted against the unguarded query so the assertion stays about the channel rather than the
  fixture.

### Known limitations

- **The floor cannot see past the current page.** The database pages and computes `TotalCount`
  before suppression runs, so a page can come back short and the total will have counted groups that
  were dropped. Nothing protected leaks; the numbers do not reconcile. Fixing it means moving the
  floor into the generated SQL, which is the pipeline this phase must not touch.
- **A summary always groups.** The pipeline requires at least one grouping field, so "one group over
  everything" is a shape that cannot be asked for — which is why the floor says nothing about it.
- **`RuleRequest` still cannot express a transform**, so an operator setting a floor through the
  admin API is not possible over HTTP. A store client can.
- **The aggregate denial overrides an explicit allowance, including a sealed one.** The opt-in is on
  the attribute that transforms the field, which is where the author who chose to obscure it can
  weigh that against being able to count it.

### What Phase 9 inherits

- **Design §7 needs two corrections**: §7.2's mitigation covers every transform rather than masks
  alone, and the five channels are seven mitigations. This joins §5.1's four missing fields, §5.2's
  missing `LoadNarrowAsync`, §5.7's sealed-field sentence and §3.4's claims-adapter shape.
- **`PolicyInferenceTests` is the security regression suite and is complete.** Twenty-four tests,
  every one of the seven mutation-checked.
- **Three new options to document**: `MinGroupSize`, and `AllowAggregate`/`MinGroupSize` on the six
  transform attributes. The k-anonymity story is the one a reader will not guess from the API.

## Phase 7 outcome

Closed 2026-09-06, 11 commits. 1375 tests pass, up from 1179. `dotnet build -c Release` across the
whole solution emits zero warnings, and the four named files are untouched — as is the whole of
`Source/`.

Plan: [2026-09-06-policies-v3-phase-7-platform.md](2026-09-06-policies-v3-phase-7-platform.md).

### Four decisions settled before implementation

1. **The admin API refuses to map without a named authorization policy**, and refuses at startup
   rather than at request time. Read and write take separate policies.
2. **Rules may set all four new facts, with `Overridable` as the ceiling.** Taken stronger than
   either option offered: rather than choosing which facts a rule may carry, the existing
   sealed/overridable flag decides — so a sealed `[DwCost(10)]` cannot be undercut and an
   overridable one can.
3. **Audit events buffer on the context and drain once per request**, so nothing is fire-and-forget
   and nothing blocks the query path.
4. **Outbound alias renaming ships**, on the surfaces whose rows this library generates.

### Public API added

- `Policies.Attributes` — `DwDescribeAttribute`, `DwAllowedValuesAttribute`, `DwCostAttribute`,
  `DwAuditAttribute`
- `Policies.DTOs` — `FieldFacts`, `PolicyExplanation`, `FeatureExplanation`;
  `FieldPolicy.Label/.Description/.Group/.Order/.AllowedValues/.CostWeight/.AuditedFeatures/.IsAudited`
- `Policies.Audit` — `DwAuditEvent`, `IDwAuditSink`; `DwPolicy.DrainAuditAsync`
- `Policies.Discovery` — `DwEntityCatalog`, `PolicySchema`, `PolicySchemaField`,
  `PolicySchemaBuilder`, `PolicySimulator`, `PolicySimulation<T>`
- `PolicyResolver.Explain`; `DwPolicy.Resolver`, `DwPolicy.StoreProviders`;
  `StorePolicyProvider.LoadedAt/.Age/.LastError`; `DwPolicyOptions.Entities`;
  `DwCaps.MaxQueryCost/.DefaultFieldCost/.MaxAuditEvents`; `PolicyErrorCode.QueryCostExceeded`
- `DynamicWhere.ex.Policies.AspNetCore` — `MapDwPolicyAdmin`, `DwPolicyAdminOptions`,
  `DwClaimsOptions`, `DwClaimsAdapter`, `RuleRequest`, `DwPolicyAuditMiddleware`,
  `UseDwPolicyAudit`, `GetPolicyContextAsync`

### A carrier fragment was winning elections it never entered

The phase's headline finding, and it predates the phase. `[DwAlias]`, `[DwOperators]`,
`[DwRequireWhere]` and `[DwForceWhere]` each emitted a fragment claiming `PolicyFeature.Where` with
`PolicyEffect.Allow`. An attribute is sealed unless its author says otherwise, and a sealed
allowance outranks every runtime denial — so **decorating a field with an alias made that field
impossible for any rule to deny**. The same held for an operator restriction, a filtering
requirement, and a tenant scope.

Both attributes' own doc comments describe the intended behaviour — *"this fragment losing the
contest for `Where` does not discard the name"* — so the code and its documentation disagreed, and
the documentation was right. The fragments assumed they would lose a contest they always won.

All four now speak to `PolicyFeature.None`, which covers nothing and wins nothing, while their
typed carriers are elected, intersected and accumulated exactly as before. `PolicyFeature.None` is
therefore a legal value on a fragment now, and `PolicyRule`'s blanket refusal of it splits in two:
a rule stating nothing at all is still refused, and a rule naming an effect with no feature to apply
it to is refused separately — that being the shape where an operator means to deny a field,
mistypes the features, and gets a stored rule that does nothing while the admin surface lists it as
a denial.

### Four more found by reading the platform

Twenty-four of this shape now, across seven phases.

- **The schema resolved a nested field against the wrong type.** `Contact.Email` is a path on the
  employee and its fragments belong to that root, so resolving it against the contact found nothing
  — and a field with no fragment is permitted. A denied field was advertised as filterable to the
  front end that builds its UI from exactly that advertisement.
- **`POST /rules` let a client choose who wrote a rule.** Binding `PolicyRule` from the body meant a
  request could carry its own `createdBy`, forging the attribution of the rule it was writing. An
  audit column a client can set is not an audit column. `RuleRequest` carries none of the four.
- **The sealed-field check on that endpoint resolved nothing.** `SealedFields.Refuse` takes a type
  resolver; the endpoint handed it the catalogue's, and the catalogue answered only to public names
  while a rule carries `Type.FullName` by design. The resolver returned null, the check accepted
  silently, and a rule aimed at a sealed field was stored and read back as a control in force.
- **The simulator's runtime dispatch wrapped every escaping exception.** `MethodInfo.Invoke` raises
  a `TargetInvocationException`, so a host catching a specific type never matched and a malformed
  clause left as a five-hundred. A dispatch mechanism must not change the exception a caller sees.

### Decisions worth not re-litigating

- **Everything an endpoint answers with is computed in the core package.** The schema walk needs
  `CacheReflection`, which is internal, and the explanation needs the four precedence rules — a
  second copy of either is how the two drift, and the drift here is a schema that advertises what
  the query refuses, or an explanation that contradicts the decision.
- **`Gate.PolicyFor` takes the feature it is resolving for.** That is the structural half of the
  audit: a new queryable surface cannot resolve a policy without naming a feature, so it cannot
  forget to record one. Phase 3 found injection missing from three surfaces and Phase 4 found
  masking missing from nine, both because a new surface did not call something it should have.
- **The audit buffer is bounded and the bound refuses the query.** An audited field whose log has
  quietly stopped being written is the outcome the attribute exists to prevent, and a dropped
  record leaves no trace of having been dropped. This cap is a resource guard rather than a policy
  decision, so a dry run does not suspend it.
- **A simulation records no audit events.** It consults the policy exactly as a real query does and
  would otherwise write the caller into the log kept to establish which accesses happened, for a
  read that never occurred. It runs on a copy of the context that shares the pinned snapshots, so
  the answer is about the policy actually being served.
- **The entity catalogue is a security boundary.** An endpoint resolving a name straight to a type
  would let whoever reaches it enumerate every type the process has loaded. An unknown name and an
  unexposed one answer identically.
- **Every reference is charged, not every distinct field.** Charging per field leaves a caller free
  to generate the same work by naming one column a thousand times, which is the case the cap exists
  for. The running total is a `long`, because two caller-influenced numbers multiplied in `int`
  arithmetic wrap back under budget — a refusal turning into a grant at exactly the size the cap is
  for.
- **`/explain` reports every tied source.** This closes Phase 1 Task 11: fragments equal on all four
  passes are equal in force, and crediting one would name a rule that contributed no more than its
  twin.

### Known limitations

- **§5.7 says sealed fields never appear in `/schema`; the rule implemented is narrower.** Taken at
  its word it also hides a masked field — sealed, and still filterable, sortable and readable —
  leaving a front end unable to offer a field whose queries succeed. A field appears when the caller
  can do at least one thing with it, so `[DwDenied]`, the section's own example, appears nowhere.
- **Outbound renaming covers four methods.** `ToListDynamic`, `ToListAsyncDynamic` and the two
  summary terminals materialize rows of a generated type. `FilterResult<T>` holds the caller's own
  type and cannot be renamed at all.
- **`/explain` without a field covers only fields the caller can use**, because it walks the schema.
  A fully denied field is explained by naming it — which is refused, on the same reasoning that
  keeps it out of the schema. An operator needing that answer reads the rule listing.
- **`RuleRequest` cannot express a transform, an operator restriction, a forced predicate or a
  set of facts.** Those carriers have shapes of their own and the wire contract for them is not
  designed. A store client can still write them; the endpoint writes the rest.
- **`DwPolicy.Configure` is process-wide and single-shot**, so a host cannot reconfigure the
  administrative surface without a restart. That is the posture `DwPolicy` was built for and is
  unchanged here.

### What Phase 8 inherits

- **`MinGroupSize` has a budget to live beside now.** `DwCaps` gained three members this phase and
  the pattern for a cap — frozen at startup, refused below one, reported through the trace — is
  settled.
- **The audit sink is where an inference attempt should be recorded.** `PolicyInferenceTests` holds
  two closed channels and completes to seven here; an attack that is blocked is exactly the access
  a log should carry.
- **`PolicySimulator` is how an inference test asks "what would this do"** without building a
  database fixture for it.

### What Phase 9 inherits

- **The release ships four packages now.** `publish.yml` packs all four, `build/check-version.ps1`
  fails when their versions disagree, and the solution holds them. Verified by packing: eight
  artifacts, and the AspNetCore package declares `DynamicWhere.ex` at the matching version with a
  framework reference rather than pinned ASP.NET Core packages.
- **§5.7's endpoint list is right and its sealed-field sentence needs a footnote**, on top of §5.1's
  four missing fields and §5.2's missing `LoadNarrowAsync` that Phase 6 recorded.
- **§3.4 writes the claims adapter as `DwPolicyContext.FromClaims(principal)`.** It ships as
  `DwClaimsAdapter.CreateContextAsync` plus an extension method, because C# has no way to add a
  static to a type in another assembly.
- **The EF Core 6.0.22 leg covers four projects now.** The AspNetCore package targets the same
  floor and takes a framework reference rather than package references.

## Phase 6 outcome

Closed 2026-09-06, 8 commits. 1179 tests pass, up from 1072. `dotnet build -c Release` across the
whole solution emits zero warnings, and the four named files are untouched — as is the whole of
`Source/`.

Plan: [2026-09-06-policies-v3-phase-6-providers.md](2026-09-06-policies-v3-phase-6-providers.md).

### Four decisions settled before implementation

1. **One whole-rule serializer, refusing what it cannot carry.** §5.1 gives a rule one payload
   column, for the transform, and the rule carries four more things. See below.
2. **The conformance suite runs against real infrastructure**, not a fake: Redis and PostgreSQL in
   containers, plus a SQLite leg. A fake proves the fake works, and the property the suite exists
   for is that three real stores behave alike.
3. **The package ships the model and no migrations.** A migration is per provider, so shipping one
   contradicts §5.6's own claim. The two entity configurations are public, so the tables can join a
   migration history the consumer already runs.
4. **The two new packages version in lockstep with the core**, and the release plumbing was extended
   in this phase rather than in Phase 9.

The first, third and fourth were taken as offered; the second was taken stronger than proposed —
a real PostgreSQL server was added to what had been a SQLite-only relational leg, and it earned its
place twice over within the hour.

### Public API added

- `Policies.Storage` — `PolicyRuleDocument`, `RuleDetail`, `SealedFields`,
  `PolicyRule.NormalizeSubjectKey`
- `DynamicWhere.ex.Policies.Redis` — `RedisPolicyStore`
- `DynamicWhere.ex.Policies.EntityFrameworkCore` — `EfPolicyStore`, `DwPolicyRuleRecord`,
  `DwPolicyVersionRecord`, `DwPolicyRuleConfiguration`, `DwPolicyVersionConfiguration`,
  `DwPolicyDbContext`

### Design §5.1 is short by four fields, and three of them fail open

This is the phase's headline finding, and it was in the specification rather than the code. §5.1
gives a rule one `Payload` column — "mask spec, default value, bucket step" — and `PolicyPayload`
reads exactly that. The `PolicyRule` Phase 5 shipped also carries `Forced`, `RequiredOperators`,
`AllowedOperators` and `Alias`.

| Carrier | What dropping it does |
|---|---|
| `Forced` | A tenant scope stops being injected — cross-tenant disclosure |
| `RequiredOperators` | The demand to filter disappears; the field is readable unscoped |
| `AllowedOperators` | `null` means "says nothing", so the restriction becomes no restriction |
| `Alias` | Cosmetic, and the only one of the four that is |

`InMemoryPolicyStore` never exposed this because it holds `PolicyRule` objects and never serializes
one, and **no store test exercised any of the four**. This was the first phase in which a rule left
the process. `PolicyRuleDocument` now carries all of them, and each of the three is
mutation-checked separately: dropping it from the writer turns the suite red on its own.

### Zero-defaulting reaches two more enumerations

Phase 5 recorded four axes whose zero member is the permissive one. Serialization adds two more:
`Operator.Equal` is `0` and `DataType.Text` is `0`, so an unparsed operator restriction reads as a
plausible-looking equality restriction nobody wrote, and a forced predicate reads as the wrong type
to validate its value against. Every enumeration is therefore written by name and read by name, in
JSON and in a database column alike, through one parser both providers share — and a string of
digits is refused as firmly as a blank.

### Three defects found by reading, none by a test

Twenty of this shape now, across six phases.

- **The relational zones did not partition the table.** Broad asked for `SubjectKind <> 'User'` and
  narrow asked for `SubjectKind = 'User'` *and* a normalized key in the caller's set — so a user row
  whose normalized key was null belonged to neither query. It loaded into no zone, threw nothing, and
  left the snapshot one denial short of what the table held. The test written for it failed with "no
  exception was thrown", which is what invisible looks like. Broad now asks for everything narrow
  could never return, so such a row reaches `StoreSnapshot`'s existing refusal. This matters *because*
  the package ships no migrations: the consumer generates the schema, and can generate one this
  library did not intend.
- **The Redis transaction's result was discarded.** `ExecuteAsync` returns false when the transaction
  was abandoned and the queued commands never ran, so an upsert would return the rule and publish a
  version with the hash untouched — an operator told a control is in force when nothing was stored.
  The queued command tasks were dropped too, turning a command that failed inside a committed
  transaction into an unobserved exception and a silent success.
- **A rule document whose detail was a JSON scalar** threw `InvalidOperationException` out of
  `TryGetProperty` rather than the `ArgumentException` the contract names. Fail-closed, but not a
  refusal a caller could write a catch for.

### Corrections made during execution

- **The version must be read before the rules, in both stores.** Read after, a write landing in
  between stamps the snapshot with a version *newer* than the rules it holds — and since the poll
  compares versions, the instance sees no change and serves the previous policy indefinitely. Read
  first, the same race stamps a version older than the rules and costs one redundant reload.
- **`dotnet pack` takes one project.** The first attempt at the release plumbing passed three and
  would have failed the release rather than the build. Found by running it.
- **A retry budget of four was not enough under contention.** Twelve concurrent writers exhausted it
  against a real PostgreSQL server: every loser of a round retried at the same instant and collided
  again, so the contention never decayed. Backed off with jitter and raised to eight. SQLite could
  not have shown this, which is the whole argument for the container leg.

### Decisions worth not re-litigating

- **A row that cannot be read fails the load; it is never skipped.** A skipped row is a denial that
  has stopped applying with nothing reporting it. Failing routes into machinery that already exists:
  fatal at startup, and afterwards a refresh failure bounded by the staleness ceiling.
- **The relational row is a `DwPolicyRuleRecord` and never a `PolicyRule`.** EF materializes by
  setting properties, which would walk past every boundary refusal Phase 5 mutation-checked — the
  guards that exist precisely for a rule arriving from a database.
- **Subject identities are normalized on write in both stores, through one shared normalizer.**
  Redis matches keys byte for byte; a relational `=` honours the column's collation, which is
  case-insensitive on SQL Server's default and case-sensitive on PostgreSQL. Without this the same
  rule applies on a developer's SQL Server and not on the production Postgres.
- **Instants are stored as UTC.** Npgsql refuses a `DateTimeOffset` with a non-zero offset for
  `timestamptz`, so a rule valid from midnight in Baghdad would save on one provider and throw on
  another. Nothing this library reads is lost: validity is compared as an instant.
- **The Redis publish is the fast path and the poll is the mechanism.** `GetVersionAsync` reads the
  counter for real; mutation-checking a stubbed version turns three tests red.

### Known limitations

- **The suite now needs a Docker daemon, and `publish.yml` runs it before packing** — so a daemon is
  required to cut a release, not merely to build. `ubuntu-latest` supplies one. It fails rather than
  skipping when none is available, deliberately: a conformance leg that quietly does not run reads
  as three stores agreeing when only one was checked.
- **`EntityType` and `FieldPath` are capped at 512 characters.** SQL Server and PostgreSQL both
  throw on overflow rather than truncating, so the failure is loud on both named providers; a
  provider that truncates silently would turn a long generic type name into a rule matching nothing.
- **A user row with no normalized key is refused rather than repaired.** There is no query that
  could find such a row on behalf of its own caller, so failing the load is the only honest answer.

### What Phase 7 inherits

- **Two more stores to register, and neither is disposable.** `RedisPolicyStore` does not own its
  multiplexer and `EfPolicyStore` creates a context per operation, so the admin surface wires them
  as singletons over resources the host already owns.
- **`POST /rules` gets its configuration-time sealed-field check for free** — `SealedFields.Refuse`
  is shared, so all three stores refuse identically and the endpoint inherits whichever is
  registered.
- **The audit columns are populated by whoever writes the rule, and nothing sets them today.**
  `CreatedBy` and `UpdatedBy` round-trip through both stores and are always null unless a caller
  supplies them; the admin surface is where a principal is actually known.

### What Phase 9 inherits

- **The release ships three packages now.** `publish.yml` packs all three and
  `build/check-version.ps1` fails when their versions disagree, so the version bump is one number in
  three csproj files. Both were verified by packing: six artifacts, and the providers declare
  `DynamicWhere.ex` at the matching version from their project reference.
- **§5.1 and §5.2 both need correcting**, on top of the `Effect` column Phase 5 already recorded:
  §5.1's rule shape is missing four fields, and §5.2's contract is missing `LoadNarrowAsync`.
- **The EF Core 6.0.22 leg covers three projects now**, not one. Both new packages target the same
  floor.

## Phase 5 outcome

Closed 2026-09-06, 14 commits. 1072 tests pass, up from 911. `dotnet build -c Release` across the
whole solution emits zero warnings, and the four named files are untouched.

Plan: [2026-09-06-policies-v3-phase-5-store.md](2026-09-06-policies-v3-phase-5-store.md).

### Five decisions settled before implementation

1. **A context is prepared asynchronously, and an unprepared one is refused.** Design §3.6 already
   said to build it through an async factory; what it did not say is what happens to a context that
   skipped one, and that was the whole security question. Refused unconditionally — no carve-out for
   a caller holding no user subject, because that carve-out is correct today and is the shape that
   has failed open on this branch four times. Pinning the snapshot per context also delivers §5.3's
   atomicity for free.
2. **Every enum is rejected at the store boundary, not just the level.** See below.
3. **Transform detail is typed at the boundary, parsed once, never on the query path.** §5.1's JSON
   payload is read by one shared routine that throws rather than ever returning a null stage.
4. **`FailClosed` throws.** A wildcard `Deny` is not fail-closed: `[DwOperators]` emits a sealed
   `Allow` on `Where`, which outranks a `DynamicGlobal` denial, so every field carrying an operator
   restriction would have stayed filterable in exactly the state the mode exists to refuse.
5. **Validity is evaluated per query; enablement is baked at load.** A window resolved at load
   expires only when the next refresh succeeds — under `LastKnownGood` with a dead store, that is
   the whole `MaxSnapshotAge` window past its own expiry.

All five were Sajjad's, taken as offered.

### Public API added

- `Policies.Storage` — `PolicyRule`, `StoreSnapshot`, `NarrowZone`, `IDwPolicyStore`,
  `IDwPolicyWritableStore`, `IDwPolicyRefresher`, `InMemoryPolicyStore`, `PolicyPayload`
- `Policies.Resolution` — `StorePolicyProvider`, with `CreateAsync`, `PrepareAsync`, `RefreshAsync`,
  `Version` and `IsDegraded`
- `Policies.Enums` — `StoreFailureMode`; `PolicyErrorCode.StoreUnavailable` and
  `.PolicyContextNotPrepared`, appended at 17–18
- `DwPolicyOptions.StoreFailure`, `.MaxSnapshotAge` and `.RefreshInterval`; `DwPolicy.PrepareAsync`

### The zero-default trap was wider than recorded

The roadmap carried one instance, on `PolicyLevel`. Three more were live, and the combination is
worse than any of them alone:

| Axis | Default | What an absent or unparsed column means |
|---|---|---|
| `PolicyLevel` | `0` | Outranks a sealed attribute |
| `PolicyEffect` | `Allow = 0` | A grant |
| `DwSubjectKind` | `Global = 0` | Applies to every caller |
| `PolicyFeature` | `None = 0` | Covers nothing, so the rule is inert |

**A rule row of all zeroes reads as "grant everyone everything, above sealed".** All four are
refused, and the level is not a column at all: it is derived from the subject through a switch with
a throwing default arm, so zero is unreachable rather than guarded against. `PolicyFeature` cannot
use `Enum.IsDefined` — `Where | Select` is `3` and is a member of nothing — so it is masked against
`All` and `None` is refused outright.

Each guard was mutation-checked alone. Removing the effect check, the unknown-bit mask or the `None`
refusal turns `PolicyRuleTests` red. The subject-kind check does not, because the switch refuses the
same case; both were then removed together and the suite went red, so the redundancy is real rather
than one guard doing nothing.

### Defects and corrections

- **Four fail-open holes were found by reading the code, none by a test.** A user subject added
  *after* preparation left that user's rules unread; `StoreSnapshot.For` handed out the live
  `List<PolicyRule>` behind its `IReadOnlyList`, so one cast and one `Clear()` removed a denial
  process-wide; the watch skipped a version lower than the one held, so a restore from backup kept
  serving withdrawn rules until a poll noticed; and `PolicyRule` held the caller's operator arrays
  by reference inside a process-lifetime snapshot. Seventeen of this shape now, across five phases.
- **The staleness ceiling was measured from the timestamp the *store* reported.** A store clock
  running behind would only have failed closed, but one running ahead would make every snapshot look
  fresher than it is and silently extend the ceiling — a database stamping the row with its own
  server time is enough. The provider stamps its own load time now;
  `StoreSnapshot.LoadedAt` stays as the store's record and decides nothing.
- **The refresh watched or polled, never both.** No notification channel is guaranteed to deliver —
  Redis pub/sub in Phase 6 is fire-and-forget — so an instance missing one message would have served
  the previous policy until something else happened to change it. It polls behind the watch now,
  which bounds a dropped notification at one interval instead of forever.
- **Design §5.2's store contract is incomplete.** It lists three members, and §5.3 splits user rules
  out of the snapshot so they are fetched per caller — with nowhere to fetch them from.
  `LoadNarrowAsync` is a fourth member and is not optional.
- **Design §5.1's `Effect` column is out of date.** It lists `Mutate | Default | Generalize` as
  effects; `PolicyEffect` has only `Allow`, `Mask` and `Deny`, because Phase 4 settled transforms as
  typed stages carrying `PolicyEffect.Mask`. Correct the document in Phase 9.

### Decisions worth not re-litigating

- **A stored payload may not name a transformer type**, in either direction. It escalates a store
  from "can change policy" to "can construct arbitrary types", which is a different thing from the
  disclosure the sealed ceiling bounds, and §5.1 names only a mask spec, a default value and a
  bucket step as the payload's purpose. `[DwMutate]` is source code and is reviewed as such.
- **A rule names its entity by `FullName`, and a name without a namespace is refused.** Matching on
  the short name collides across namespaces, and over-applying an `Allow` is a grant nobody wrote;
  matching only on `FullName` would turn a short name into a rule that silently never matches. The
  refusal converts a silent miss into a rejected upsert.
- **Upsert cannot always check sealed fields, and resolution is what enforces them.** The
  configuration-time check needs a `Type` and a store holds a string, so `InMemoryPolicyStore` takes
  an optional resolver. Without one it accepts, and the rule is inert. Both halves are tested
  separately so the weaker one cannot be mistaken for the whole.
- **A purpose-bound rule applies only on a match.** That makes it a way to narrow a grant; a denial
  that must always hold is written without a purpose.

### Known limitations

- **Fragments are rebuilt per field per query.** `Resolve` calls `GetFragments` once per field, so a
  wide entity against a large snapshot re-scans that entity's rules each time. Correct, and not yet
  measured. If Phase 9's benchmarks show it, the fix is to cache the subject-and-purpose-filtered
  candidates on the attachment — those are time-independent — and keep evaluating `AppliesAt` live.
- **A context pinned longer than `MaxSnapshotAge` is refused even after the provider refreshed.**
  Deliberate: the ceiling measures what the caller is being served. It is also the reason a context
  is built once per request.

### What Phase 6 inherits

- **The conformance suite is an abstract base.** Derive from `PolicyStoreConformanceTests` and
  supply a factory; Redis and EF inherit all eleven tests, including the failure modes, which drive
  a decorator rather than a per-store hook.
- **`PolicyPayload` is the only place a payload is read or written.** Neither store provider should
  parse JSON of its own, or the three would drift into three standards.
- **Poll behind the watch.** Redis pub/sub is fire-and-forget and the poll is what bounds a dropped
  message. Do not implement the Redis store as watch-only.
- **A store must not return user rules from `LoadAsync`, nor broad rules from `LoadNarrowAsync`.**
  Both zones refuse the other's rules at construction rather than filtering them out.
- **Stamp `StoreSnapshot.LoadedAt` however is convenient; it decides nothing.** The provider measures
  staleness on its own clock, which is what keeps a database's server time out of the ceiling.

### What Phase 7 inherits

- **`/explain` has a real second source now.** Phase 1's tie-attribution note becomes visible the
  moment two rules tie, and `PolicySource.FromRule` carries the rule id and subject to report.
- **`/schema` owns friendly entity names.** Rules match on `FullName` by design; the short-name
  lookup an operator wants belongs where the public vocabulary is the subject.
- **`GET /dw-policies/health` has its inputs.** `StorePolicyProvider.Version`, `.IsDegraded` and the
  attachment's load time are what that endpoint reports.

## Phase 4 outcome

Closed 2026-08-28, 5 commits. 911 tests pass, up from 816. `dotnet build -c Release` across the
whole solution emits zero warnings, and the four named files are untouched.

Plan: [2026-08-28-policies-v3-phase-4-transformation.md](2026-08-28-policies-v3-phase-4-transformation.md).

### Six decisions settled before implementation

1. **Transforms compose, in a fixed sequence** — mutate, generalize, format, mask, truncate. Custom
   logic sees the real value, precision drops before rendering, characters are hidden after
   rendering, and a length cap has the last word.
2. **`[DwDefault]` short-circuits.** Masking a value already replaced by `"N/A"` obscures nothing,
   and composing them would make the output depend on an ordering rule invisible from the entity.
3. **Applicability is a type rule, not a table.** A chain is valid only when its output is
   assignable to the member it decorates. The documented matrix is derived from that one check, so
   there is no second source of truth to drift.
4. **Composable methods return the handle**, so a chain never leaves the guard and the terminal call
   transforms. `AsUnguardedQueryable()` is the named, greppable way out.
5. **Summaries group on real values and transform on the way out**, with a collision refused. This
   was Sajjad's design and it is better than the three options offered: grouping in SQL keeps the
   counts correct, and transforming the keys afterwards keeps a masked field groupable at all.
6. **`ValidatePolicyModel()` ships here**, since most of design section 4.8 is mask-and-type
   compatibility and nothing was checkable until masks existed.

### Public API added

- `Policies.Enums` — `MaskStrategy` (8; `Tokenize` stays deferred), `GeneralizeMode`, `DatePart`;
  `PolicyAction.Mutated`, `.Defaulted`, `.Generalized`; `PolicyErrorCode.AmbiguousGroupKey` and
  `.TransformRequiresMaterialization`, appended at 15–16
- `Policies.Attributes` — `DwMask`, `DwMutate`, `DwDefault`, `DwGeneralize`, `DwTruncate`, `DwFormat`
- `Policies.Masking` — `IValueTransformer`, `DwTransformContext`
- `Policies.DTOs` — `TransformKind`, `TransformStage` and its six derived stages, `ValueTransform`;
  `TypePolicy.Transforms`; `PolicyFragment.Transform`; `FieldPolicy.Transform` and `.IsTransformed`
- `Policies.Validation` — `PolicyModelValidator`, `PolicyModelReport`; `DwPolicy.ValidateModel`
- `DwPolicyOptions.HashSalt` and `.Services`
- `PolicyQueryable.AsUnguardedQueryable`; the composable methods now return the handle

Transform stages are **elected per stage**, so a runtime rule can add a truncation on top of a
sealed mask and cannot replace the mask. Electing the whole chain would let a rule discard a
compile-time mask by supplying anything at all.

Transform fragments carry `PolicyEffect.Mask` on `Select`, which makes `FieldPolicy.IsMasked` real
for the first time — nothing could produce a `Mask` effect until now — and keeps a masked field
filterable and sortable, since `Allows` refuses only a denial. A denial on the same field still wins
the election, so a field both denied and masked is dropped rather than masked.

### Defects and corrections

- **The composable surface would have leaked every masked value.** Nine methods returned an
  `IQueryable` the *caller* materializes, so `ToList(f)` masked and `Filter(f).ToList()` did not,
  one word apart. The same shape as the Phase 3 injection hole, one layer down. Closed by returning
  the handle; the four that yield a non-generic `IQueryable` refuse outright on a transformed type.
- **`MaskStrategy.Email` disclosed the mailbox and domain lengths**, ignoring `PreserveLength`.
  Found by a wrong expectation in a test that turned out to be right about the principle.
- **The design document is wrong about `[DwDefault]` coercion.** Section 4.4 says constants are
  converted by the existing `Normalizer`, "direct reuse, no new conversion code". `Normalizer`
  converts a value *to* a string, and nothing else in the library turns a string into a CLR value —
  the query path hands its literals to the dynamic-LINQ parser. The conversion had to be written.
- **Reference identity in the walker is load-bearing, and the obvious test did not prove it.** A
  shared-reference test passes either way, because an ordinary class already has reference equality.
  A *record* does not: two rows holding the same value are one object to a set keyed on equality, so
  one keeps its real value. Proven by mutation only after the weaker test was replaced.

### Decisions worth not re-litigating

- **The walk is driven by the policy's paths, not by the object graph.** The paths needing
  transformation are known before a row is read, so the walker navigates exactly those. It is
  cheaper on a wide entity and, more importantly, cannot miss a path by failing to recognise a
  navigation — which is what went wrong five times on the input side. It also makes a cycle guard
  unnecessary: a path has finite length, and shared objects are handled by reference identity.
- **A path that should be reachable and is not fails the query.** Skipping would emit the value
  exactly as stored. A *null navigation* is not that case and is skipped, because there is no value
  beneath it.
- **Nothing in the pipeline catches.** A transformer that throws fails the query. Catching it into
  "return what you were given" hands the caller the real value, which is the one outcome a transform
  must never produce.
- **Every mask strategy fails closed on an input that does not fit it** — an address that is not an
  address, a partial mask whose kept ends cover the value, a phone number too short to keep digits
  from. All are masked in full rather than returned intact.
- **Masked-but-orderable is a warning, not an error.** Sorting runs against the real value, so
  paging ranks the true order; but there are models where the ordering is the point, and the engine
  does not get to decide.

### Known limitations

- **Outbound alias renaming is still not delivered.** Phase 3 deferred it here on the reasoning that
  the result transformer would own the materialized shape. It does, but renaming a column is not
  mutating a value: a generated dynamic type bakes its property names in at query-build time, so
  renaming means re-projecting into a second generated type. It belongs with `/schema` in Phase 7.
- **`SelectDynamic`, `FilterDynamic`, `Group` and `Summary` are refused on a transformed type.**
  They cannot return the handle, because what they yield is no longer a sequence of `T`. The
  terminal equivalents work, and `AsUnguardedQueryable()` is the deliberate way past.
- **A chain containing `[DwMutate]` is not statically checked**, because a transformer returns
  whatever it likes. The run-time assignability check still catches it.
- **`ValidateModel` takes explicit types** rather than scanning assemblies. An application knows its
  entity types, and a scan would wander into every referenced package.

### What Phase 5 inherits

- **The `default(PolicyLevel)` gate is now overdue.** `PolicyLevel` has no member with value `0`, so
  a level read from a column or a JSON payload defaults to `0` and outranks `SealedAttribute = 1`.
  Unreachable until a store exists. Reject an unmapped level at the store boundary.
- **Do not cache `TypePolicy` per type.** Aliases and transforms are both per-caller now.
- **A store may set a transform stage**, and `PolicyFragment` already refuses one on the wildcard
  path. The store boundary should refuse the same rather than relying on the fragment constructor.
- **`TransformerCache` holds one instance per type for the process.** A store that can change which
  transformer a field uses does not invalidate it; the cache is keyed on the type, not the rule, so
  that stays correct — but a store that could introduce a *new* transformer type at runtime makes
  `ValidateModel` a startup-only guarantee rather than a total one.

### Closed after Phase 3 — two inference channels, early

Design sections 7.1 and 7.5, shipped in `fe5e33b` between Phases 3 and 4. Both were live on the
branch: injection put a real tenant predicate into the text `getQueryString` hands back, and
`Segment` participation gating refuses a field denied for `Segment` while saying nothing about a
field denied only for `Select`.

- **7.1** — in the strict tier, a field denied for `Select` is refused in a segment's conditions, at
  every depth, in every set. Strict only, because the rule refuses a filter that is legitimate
  outside a set operation and the convenience tier's caller is the project's own front end.
- **7.5** — the strict tier refuses `getQueryString` on all six methods that can return it, before
  the sanitizer runs so a refused request does no work. The convenience tier still returns it,
  documented.

`FieldDeniedForSegment` is reused for 7.1 rather than adding a code: the refusal genuinely is "this
field is refused inside a set operation", and the `SourceOrigin` carries why. `QueryStringDenied` is
new, appended at 14.

Verified by mutation — disabling both mitigations turns 6 of the 11 new tests red and leaves green
exactly those asserting behaviour that is still allowed.

### What Phase 4 inherits

- **Outbound renaming on the dynamic surface.** The result transformer owns the materialized shape
  and is the right home for it.
- **Mask on canonical paths, never on caller spellings.** Alias resolution is complete before
  `Validate<T>()` sees anything, so everything downstream of the sanitizer works in canonical paths.
  A masking rule keyed on what the caller wrote would miss every caller who wrote the other spelling.
- **`ValidatePolicyModel()` is worth building when the mask rules join it**, since most of section
  4.8 is about mask and type compatibility.
- **`PolicyAction` still has no member any code emits for a transformation.** `Masked` exists and
  nothing emits it; `Mutated`, `Defaulted` and `Generalized` do not exist yet. Add them in the change
  that makes something able to produce them, as Phase 2 did for `Injected`.
- **Do not reintroduce a per-type cache for `TypePolicy`.** Aliases are per-caller now. Phase 5's
  store faces the same trap from the other direction.

### What Phase 3 inherits

- **The alias axis is now three-valued.** `[DwAlias]` adds a fourth spelling to a system where three
  already exist and one of them was missed. Whatever Phase 3 adds must be resolved *before*
  `Validate<T>()` sees a path, and must be gated through whatever it stands for. `BuildReferences`
  in `FilterSanitizer` is the existing precedent.
- **Injection must not reuse `SanitizeClause`.** Forced predicates are never gated against the
  caller's own policy, and `SanitizeClause` gates everything it is given.
- **The condition cap counts what the caller sent.** An injected predicate arrives after
  `EnforceCaps` has run, so injection can push a filter past `MaxConditions`. Decide deliberately
  whether a forced predicate spends the caller's budget; it should not.
- **`PolicyAction` has no member for an injected-and-refused case.** `Injected` exists and nothing
  emits it yet. Phase 3 is what makes it real.
