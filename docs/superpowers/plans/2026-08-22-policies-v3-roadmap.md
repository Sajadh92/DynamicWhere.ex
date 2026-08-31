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
