# Policies v3.0 — Phase 3: Injection and Aliases — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the two holes an opt-in field gate cannot close on its own. `[DwForceWhere]` injects a predicate the caller never sent, so a tenant scope applies whether or not the caller thought to ask for one. `[DwRequireWhere]` refuses a request that fails to supply a scope of its own. `[DwAlias]` decouples the public field vocabulary from the schema.

**Architecture:** Three new typed carriers on `PolicyFragment`, a type-wide sweep on `PolicyResolver` beside the existing per-field `Resolve`, and two new steps at the end of `FilterSanitizer` — inject, then verify. Alias resolution is a rewrite that runs *before* `Validate<T>()` on every clause that names a field.

**Tech Stack:** .NET 6 library, xUnit on net8.0, EF Core SQLite for integration. No new package references.

**Branch:** `feat/policies-v3`. **Baseline: 663 tests passing, zero warnings.**

---

## Four decisions taken during review

The roadmap named Phase 3 but left it unplanned. This plan was written against the code on
2026-08-27, and four forks were put to Sajjad before a line was written. All four answers are
binding and the tasks below are written to match.

### 1. `[DwRequireWhere]` is satisfied only by a conjunctively binding condition

A caller sending `Status = A OR TenantId = 5` has named the required field, and the query still
returns every other tenant's rows matching `Status = A`. Presence is therefore not the test.

A requirement is satisfied when a condition on the field sits in a group whose `Connector` is
`And` **and every ancestor group up to the root is also `And`**, using an operator from the
satisfying set. Anything else throws `RequiredFilterMissing` in both tiers.

The default satisfying set is the positive membership operators — `Equal`, `IEqual`, `In`, `IIn`.
`NotEqual`, `IsNull`, and the range operators each satisfy the letter of a scope while inverting or
widening it. `[DwRequireWhere(Operators = ...)]` overrides the set where a field genuinely wants a
range.

### 2. `[DwAlias]` adds a spelling; it does not remove one

The internal path keeps working after a field is aliased, so adding an alias is never a breaking
change to a filter already in production. Hiding the internal name is Phase 7's `/schema` job — it
advertises the alias and does not advertise the path.

The cost is that both spellings must be gated. That is the `BuildReferences` precedent from
Phase 2: a name maps to a *list* of paths and every one of them is gated.

### 3. A refusal reports the spelling the caller used

`PolicyException.FieldPath` carries the alias when the caller wrote the alias. Returning the
canonical path would hand a caller in the strict tier the real column path for a field they were
just refused, turning every refusal into schema disclosure — which is one of the two reasons the
spec gives for aliases existing.

The trace keeps the canonical path with the alias in the reason, through the `via` parameter
`Gate.Record` already takes. The trace is server-side and is read against the policy; the exception
crosses the boundary.

### 4. Runtime rules may set an alias, not only attributes

Chosen deliberately over attributes-only: a caller with the entitlement can rename fields to match
their own understanding of the data, which is a readability and flexibility win the compile-time
form cannot give.

Three consequences follow, and they are the reason this decision is recorded rather than assumed.

**Aliases are per-caller.** The same filter can name different fields for different subjects. Alias
resolution therefore takes the `DwPolicyContext` and cannot be cached per type the way
`AttributePolicyProvider` caches fragments.

**Collision becomes security-relevant.** A rule aliasing `"Salary"` onto `Notes`, on a type that
already has a `Salary` property, makes one name mean two paths. Neither resolution is safe to
guess: preferring the property silently ignores the rule, and preferring the alias silently
redirects a filter to a different column. An ambiguous name is refused with a new
`AmbiguousFieldName` code. This is the fail-closed reading and it is the only one available.

**Alias is elected, not accumulated.** One field has one alias per caller, decided by the same four
pass ranking the effects use, so a sealed `[DwAlias]` beats a rule and an overridable one does not.

### Scoped out, deliberately: outbound renaming

"Aliases resolve bidirectionally" in the roadmap's exit criteria is delivered in this phase as
inbound rewriting plus alias-faithful refusal and trace reporting. Renaming the *columns of a
result* is not delivered, and cannot be without breaking a standing rule:

- A typed `FilterResult<T>` cannot rename anything. `T`'s property names are `T`'s.
- A dynamic projection's names are baked into the generated type at query-build time, by the
  `Select` projection string built in `DynamicWhere.ex/Source/Extention.cs`. That file took the
  `RequirePolicy` guard in Phase 2 and the standing rule is that it takes nothing else.

`FieldPolicy.Alias` is public from this phase, so Phase 7's `/schema` can advertise the caller's
vocabulary. Outbound renaming of the dynamic surface is recorded below as a Phase 4 inheritance,
where the result transformer already owns the materialized shape.

---

## Two fail-open defects found while reviewing the spec against the code

Same shape as the eight before them: a fragment that fails to match resolves to Allow.

### Injection on the filter path alone leaves three doors open

Spec section 6.2 lists step 9 against the `Filter` pipeline and mentions no other surface. The
sanitizer has three entry points.

- `Summary` composes `GROUP BY` with aggregates. A tenant scope missing there lets a caller read
  `SUM(Salary)` across every tenant.
- `Segment` composes Union, Intersect and Except across subqueries, each becoming its own query.
  Injecting into one and not the others is worse than injecting into none: `AllRows EXCEPT
  (AllRows WHERE TenantId = 5)` returns precisely the other tenants.
- The composable methods — `Where`, `Select`, `Order`, `Page`, `Filter` — each return an
  `IQueryable<T>` the caller materializes. A scope applied to `ToList` and not to `Select` is
  bypassed by composing instead of terminating.

Every surface that can produce rows takes the injection. `Segment` takes it into **each**
`ConditionSet` independently.

### An aliased group-by key reaches `Having` and `Orders` unaliased

`BuildReferences` maps a group-by key and its dot-stripped form to the canonical path. A caller who
groups by `customer_name` has that key rewritten to `Customer.Name` during alias resolution — and
then writes `customer_name` in `Summary.Orders`, which `BuildReferences` never learned. The
reference matches nothing, the gate skips it, and a field denied for `Order` is sorted by.

This is the fourth spelling the memory warned about, arriving exactly where the third one was
missed. `BuildReferences` gains the alias mappings in the same pass.

---

## Order of operations

`FilterSanitizer` gains two steps and one that runs before everything else.

```
0.  Resolve type policy        aliases, forced predicates, required filters — one provider sweep
1.  Resolve aliases            caller's spelling -> canonical path, ambiguity refused   [NEW]
2.  Canonicalize               Validate<T>() on every path
3.  Enforce caps               counts what the caller sent
4.  Gate WHERE / operators
5.  Gate GROUP / AGGREGATE
6.  Gate ORDER
7.  Gate SELECT
8.  Inject ForceWhere          wrap root: (caller group) AND forced                     [NEW]
9.  Verify RequireWhere        against the tree as it now stands                        [NEW]
10. Emit trace
```

Three orderings in there are load-bearing.

**Aliases resolve before canonicalization** because `Validate<T>()` throws on a name that is not a
property path, and an alias is not one.

**Injection runs after gating** because a forced predicate is never gated against the caller's own
policy. It also runs after `EnforceCaps`, so an injected predicate never spends the caller's
condition budget — a filter sitting exactly on `MaxConditions` must not be refused because the
library added a term.

**Verification runs after injection**, so a `[DwForceWhere]` on a field also carrying
`[DwRequireWhere]` satisfies the requirement by supplying it. One checker reading the final tree,
rather than two checkers that could disagree.

---

## File structure

**Create in `DynamicWhere.ex/Policies/`:**

| File | Responsibility |
|---|---|
| `Attributes/DwAliasAttribute.cs` | A public name for a field |
| `Attributes/DwRequireWhereAttribute.cs` | The caller must scope on this field |
| `Attributes/DwForceWhereAttribute.cs` | The library scopes on this field regardless |
| `DTOs/ForcedPredicate.cs` | One predicate to inject, resolved |
| `DTOs/TypePolicy.cs` | The type-wide sweep: aliases, forced, required |

**Modify:**

- `Enums/PolicyErrorCode.cs` — append `RequiredFilterMissing`, `MissingContextValue`, `AmbiguousFieldName`
- `DTOs/PolicyFragment.cs` — carry `Alias`, `ForcedPredicate`, `RequiredOperators`
- `DTOs/FieldPolicy.cs` — expose `Alias`, `ForcedPredicates`, `RequiredOperators`
- `Resolution/AttributePolicyProvider.cs` — emit fragments for the three new attributes
- `Resolution/PolicyResolver.cs` — add `ResolveType`, share the election helpers with `Resolve`
- `Source/FilterSanitizer.cs` — alias rewriting, injection, verification, alias-aware reporting
- `Source/PolicyQueryable.cs` — injection on the composable surface

**Create in `DynamicWhere.Tests/Policies/`:**

| File | Responsibility |
|---|---|
| `PolicyAliasTests.cs` | Resolution on every clause, collision, per-caller aliases |
| `PolicyInjectionTests.cs` | The pinned wrapping shape, every surface, cap exemption |
| `PolicyRequireWhereTests.cs` | Binding rules, operator sets, satisfaction by injection |

---

## Task 1: The three attributes

**Files:**
- Create: `DynamicWhere.ex/Policies/Attributes/DwAliasAttribute.cs`
- Create: `DynamicWhere.ex/Policies/Attributes/DwRequireWhereAttribute.cs`
- Create: `DynamicWhere.ex/Policies/Attributes/DwForceWhereAttribute.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyAttributeTests.cs`

`DwAlias` and `DwRequireWhere` re-declare `AttributeUsage` with `AllowMultiple = false` — one name
and one requirement per field. `DwForceWhere` keeps `AllowMultiple = true`: two forced predicates on
one field are a range, and they compose by conjunction.

`DwRequireWhereAttribute.Resolve()` returns `Operators` when set and non-empty, otherwise the
default positive membership set, mirroring `DwOperatorsAttribute.Resolve()`.

`DwForceWhereAttribute` carries `Operator` positionally, plus `Value` and `ContextValue`. Setting
neither, or both, is a configuration error refused at resolution.

`DataType` is inferred from the decorated member's CLR type by the provider, which holds the
`PropertyInfo`, and there is **no override property**. C# forbids `Nullable<DataType>` as an
attribute argument, so an override would need a sentinel member on the public `DataType` enum or a
paired `Specified` flag — and it could only ever be wrong, since the pipeline validates values
against the property's own type. A CLR type with no `DataType` counterpart (`TimeSpan`, `TimeOnly`)
is refused at resolution rather than guessed.

`Operators` on `DwRequireWhere` follows `DwOperatorsAttribute.Allow` exactly: null means "unset, use
the default set", an empty array means "this set, which is empty", so a requirement declared with
no satisfying operator can never be met. Distinguishing the two is the sibling attribute's existing
contract and the fail-closed reading of an obvious typo.

- [ ] **Step 1: Write the failing tests** — construction, `Resolve()` defaults, usage flags
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): add the injection and alias attributes`

## Task 2: Error codes

**Files:**
- Modify: `DynamicWhere.ex/Policies/Enums/PolicyErrorCode.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyExceptionTests.cs`

Append `RequiredFilterMissing = 11`, `MissingContextValue = 12`, `AmbiguousFieldName = 13`. Never
renumber: the values are contract once a caller has serialized one.

`AmbiguousFieldName` is not in the spec's list. It exists because runtime aliases can collide with
a real property path, and the spec was written when aliases were compile-time only.

- [ ] **Step 1: Write the failing test**
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): add the injection error codes`

## Task 3: Fragment and policy carriers

**Files:**
- Modify: `DynamicWhere.ex/Policies/DTOs/PolicyFragment.cs`
- Modify: `DynamicWhere.ex/Policies/DTOs/FieldPolicy.cs`
- Create: `DynamicWhere.ex/Policies/DTOs/ForcedPredicate.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyFragmentTests.cs`

Typed fields, not `Payload`. The Phase 2 reasoning holds unchanged: reading a restriction back out
of an `object?` needs an unchecked cast whose failure yields null, and null here means "no
restriction".

`ForcedPredicate` is immutable and carries `FieldPath`, `Operator`, `Value`, `ContextValue`,
`DataType`. It is resolved to a `Condition` at injection time, not at construction, because
`ContextValue` reads the caller's context.

- [ ] **Step 1: Write the failing tests**
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): carry injection data on fragments`

## Task 4: The attribute provider emits the new fragments

**Files:**
- Modify: `DynamicWhere.ex/Policies/Resolution/AttributePolicyProvider.cs`
- Modify: `DynamicWhere.Tests/Policies/SecuredModels.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

Each of the three attributes becomes a fragment at the level its `Overridable` flag implies, exactly
as `DwDeny` and `DwOperators` already do. `Effect` is `Allow` and `Features` is `Where` for all
three: none of them refuses anything on its own.

New fixture types go in `SecuredModels.cs`, kept apart from `SecuredEmployee` so the fragment counts
the existing provider tests assert stay unchanged — the same discipline `SecuredAccount` follows.

- [ ] **Step 1: Write the failing tests**
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): read the injection attributes`

## Task 5: `PolicyResolver.ResolveType`

**Files:**
- Create: `DynamicWhere.ex/Policies/DTOs/TypePolicy.cs`
- Modify: `DynamicWhere.ex/Policies/Resolution/PolicyResolver.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyResolutionTests.cs`

The sanitizer needs to know every alias and every forced predicate on a type before it has a field
path to look up, which `Resolve(type, path, context)` cannot answer. `ResolveType` sweeps the
providers once and returns:

- `Aliases` — alias to the **list** of paths it could mean, `OrdinalIgnoreCase`
- `Forced` — every forced predicate that matched, in provider order
- `Required` — path to its satisfying operator set

Alias and required are **elected** per path by the existing `Outranks` ranking. Forced predicates
are **collected**: a conjunction can only narrow, and electing one would let a low-authority rule
discard a sealed tenant scope.

`Resolve` and `ResolveType` share the election helpers. Two code paths that agree only by
construction is the standing liability this project keeps paying for.

Wildcard fragments carrying an alias are ignored — one name cannot stand for every field. Ignoring
grants nothing: the alias simply does not exist and callers use the real path.

- [ ] **Step 1: Write the failing tests** — election, collection, per-caller variation, wildcard
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): resolve type-wide policy`

## Task 6: Alias rewriting in the sanitizer

**Files:**
- Modify: `DynamicWhere.ex/Policies/Source/FilterSanitizer.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyAliasTests.cs`

One function, `ResolveName`, used at every point a caller names a field. Nothing else may rewrite a
name — the whole class of defect this project keeps finding is two spellings normalized by two
routines.

The candidate set for a caller-supplied name is the alias targets it maps to, plus the name itself
when it validates as a property path on `T`. Probing the latter means catching `LogicException`
from `Validate<T>()`, which is control flow through an exception and is done deliberately: the
alternative is a second copy of the reflection walk, and a second copy is how the spellings drift.

- One candidate: rewrite to it.
- More than one: throw `AmbiguousFieldName`. Neither choice is safe to guess.
- None: leave the name alone. `Validate<T>()` refuses it downstream with the error an unguarded
  query would give, so a caller cannot probe for field existence through the policy layer.

Applied to: filter conditions at every depth, `Selects`, `Orders`; summary conditions, `GroupBy.Fields`,
`AggregateBy.Field`; segment condition sets, `Selects`, `Orders`. `BuildReferences` additionally maps
each alias to the canonical path so `Having` and `Summary.Orders` cannot reach a field through the
spelling the group-by clause used.

The alias the caller wrote is retained alongside the canonical path and passed as `via` to
`Gate.Record`, and as the reported `FieldPath` on any `PolicyException` raised for that clause.

- [ ] **Step 1: Write the failing tests** — every clause, collision, casing, per-caller, `Having` reach-through
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): resolve field aliases`

## Task 7: Injection

**Files:**
- Modify: `DynamicWhere.ex/Policies/Source/FilterSanitizer.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyInjectionTests.cs`

The shape, which is a correctness requirement and not a detail:

```
root = ConditionGroup { Connector = And, Conditions = [forced...], SubConditionGroups = [caller's group] }
```

A new root every time. Appending a forced term into the caller's own group turns
`(Status = A OR Status = B)` into `(Status = A OR Status = B OR TenantId = 5)`, which returns every
tenant's rows matching A or B. The pinned test asserts the root's connector, that its single
subgroup is the caller's group with its connector and conditions unmodified, and that its
conditions are the forced ones — written so a later "simplification" into a merge fails loudly.

A null `ConditionGroup` still produces the root. A caller who sends no filter at all is exactly the
caller a forced scope exists for.

`ContextValue` resolving to a missing key, or to null, throws `MissingContextValue` in both tiers
and in dry run. Spec section 4.3: a tenant scope that silently fails to apply is worse than a failed
request, and dry run's promise is that it changes no data — not that it grants access.

`Sort` values are assigned so the injected conditions and the wrapped subgroup satisfy
`ConditionGroup.Validate()`'s uniqueness checks. Each injected condition records
`PolicyAction.Injected`, which nothing has emitted until now.

- [ ] **Step 1: Write the failing tests** — the pinned shape first
- [ ] **Step 2: Implement for `Filter`**
- [ ] **Step 3: Run, green, commit** — `feat(policies): inject forced predicates`

## Task 8: Injection on summary, segment, and the composable surface

**Files:**
- Modify: `DynamicWhere.ex/Policies/Source/FilterSanitizer.cs`
- Modify: `DynamicWhere.ex/Policies/Source/PolicyQueryable.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyInjectionTests.cs`

The three doors from the review section. `Segment` injects into **each** `ConditionSet`
independently — a set operation missing the scope in one arm returns the complement of it.

`Select`, `Order`, and `Page` on `PolicyQueryable` currently hand their clause to the unguarded
extension directly. They gain the injected predicate, applied through `Guarded().Where(...)` before
the clause they were asked for. Injection is not routed through `SanitizeClause`: that gates
everything it is given, and a forced predicate is never gated.

- [ ] **Step 1: Write the failing tests** — one per surface, plus the Except reconstruction
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): inject across every queryable surface`

## Task 9: Required filter verification

**Files:**
- Modify: `DynamicWhere.ex/Policies/Source/FilterSanitizer.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyRequireWhereTests.cs`

Walks the tree after injection, tracking whether the path from the root to each condition is
entirely `And`. A condition on a required field satisfies its requirement when that path is
unbroken and its operator is in the satisfying set. Anything unsatisfied throws
`RequiredFilterMissing` in both tiers.

Checked on every guarded surface, including `Select`, `Order`, and `Page`. A caller asking for rows
without supplying a required scope is precisely the refusable thing, and spec section 2.5 puts a
missing `[DwRequireWhere]` in the throw-in-both-tiers row. This is deliberately *not* the
projection-synthesis reasoning from Phase 2 — there, a caller who named no projection had asked for
nothing to refuse; here, a caller who named no scope has asked for everything.

- [ ] **Step 1: Write the failing tests** — the OR case first, then nesting, operators, satisfaction by injection
- [ ] **Step 2: Implement**
- [ ] **Step 3: Run, green, commit** — `feat(policies): verify required filters`

## Task 10: End-to-end against SQLite

**Files:**
- Modify: `DynamicWhere.Tests/Policies/GuardedQueryTests.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyFixture.cs`

The injected predicate appears in generated SQL and actually removes rows. An aliased filter returns
the same rows as the same filter written with the internal path. A required filter missing throws
before the query is built.

- [ ] **Step 1: Write the failing tests**
- [ ] **Step 2: Implement whatever they expose**
- [ ] **Step 3: Run, green, commit** — `test(policies): end-to-end injection and aliases`

## Task 11: Phase gate

- [ ] `dotnet build -c Release` across the solution, zero warnings, `GenerateDocumentationFile` on
- [ ] Full suite green
- [ ] `git diff --name-only master...HEAD -- DynamicWhere.ex/Source/Builder.cs DynamicWhere.ex/Source/Validator.cs DynamicWhere.ex/Source/Converter.cs DynamicWhere.ex/Source/Normalizer.cs` is empty
- [ ] Roadmap updated with the Phase 3 outcome and what Phase 4 inherits
- [ ] Commit — `docs: record the Phase 3 outcome`

---

## What Phase 4 inherits

- **Outbound alias renaming on the dynamic surface.** Scoped out here because the projection string
  is built in `Extention.cs`. The result transformer owns the materialized shape and is the right
  home for it. `FieldPolicy.Alias` is already public.
- **`ValidatePolicyModel()` does not exist.** The spec's section 4.8 rules are enforced at query
  time and fail closed. The startup form is worth building when the mask rules join it, since most
  of section 4.8 is about mask and type compatibility.
- **`[DwForceWhere(ContextValue = ...)]` cannot be validated at startup.** What a context supplies
  is per-request. The spec lists it as a startup check; it is a query-time throw and always was.
