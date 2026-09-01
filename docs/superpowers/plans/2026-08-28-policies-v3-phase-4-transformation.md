# Policies v3.0 — Phase 4: Transformation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Values that leave the library are not always the values the database holds. A field can be masked, generalized, formatted, truncated, replaced, or handed to a caller's own transformer — after materialization, on detached objects, so a mask can never be written back as real data.

**Architecture:** Step 5 of the sandwich. The sanitizer already hands a gated `Filter` to the untouched pipeline; this phase adds a `ResultTransformer` on the way out. Transforms resolve into a chain per field, compile once per shape, and run over the materialized graph.

**Tech Stack:** .NET 6 library, xUnit on net8.0, EF Core SQLite for integration. No new package references.

**Branch:** `feat/policies-v3`. **Baseline: 816 tests passing, zero warnings.**

---

## Six decisions settled before implementation

### 1. Transforms compose, in a fixed sequence

```
Mutate  →  Generalize  →  Format  →  Mask  →  Truncate
```

Custom logic sees the real value, precision drops before rendering, characters are hidden after
rendering, and a length cap has the last word. Every stage is skipped when its attribute is absent,
so the common case — one attribute — is a one-stage chain.

### 2. `[DwDefault]` short-circuits

A field carrying `[DwDefault]` is replaced and no other stage runs. Masking a value already replaced
by `"N/A"` obscures nothing, and letting the two compose would make the emitted value depend on an
ordering rule invisible to anyone reading the entity. `[DwDefault]` alongside any other transform is
a configuration error the startup scan reports.

### 3. Applicability is a type rule, not a table

The tempting shape is a matrix of strategy against data type. The honest rule is one line:

> **A transform chain is valid only when its output type is assignable to the member it decorates.**

Everything else follows. A `decimal Salary` cannot carry `[DwMask(Full)]` because a mask emits text
and text is not assignable to `decimal`; it can carry `[DwGeneralize(Round)]`, which stays numeric,
and `[DwDefault]`, which replaces in kind. `[DwFormat]` and `[DwTruncate]` emit text, so both are
refused on anything but a text member. The matrix in the documentation is *derived* from this rule
rather than being the rule, which is what keeps the two from drifting apart.

A caller who wants a masked salary projects into a DTO with a `string` field. That is the honest
answer and it is documented rather than worked around.

### 4. Composable methods return the handle

`Filter`, `Select`, `SelectDynamic`, `FilterDynamic`, `Where`, `Order`, `Page`, `Group` and
`Summary` currently return an `IQueryable`, which the *caller* materializes — so the library never
sees the rows and cannot transform them. `guarded.ToList(f)` masks and
`guarded.Filter(f).ToList()` does not, one word apart.

They now return `PolicyQueryable<T>`, so a chain never leaves the guard and the terminal call
transforms. Leaving the guarded path requires `AsUnguardedQueryable()` — explicitly named, and
greppable in review exactly as `ApplyPolicy` is. These methods shipped in Phase 2 and nothing
outside the branch consumes them, so the signature change costs nothing today.

### 5. Summaries group on real values and transform on the way out

Rejected: refusing to group on a transformed field, and masking before grouping. Grouping runs in
SQL against real values, so the groups and their counts are correct; the *keys* are transformed as
the rows come back, along with the aggregate results, which is where a transform on
`SUM(Salary)` becomes useful rather than meaningless.

The risk this creates is collision: two distinct real keys can transform to the same output, leaving
a result with duplicate keys whose counts do not add up. That is refused, not merged —
`AmbiguousGroupKey`. Merging would invent a number nobody computed, and leaving it would hand back a
table that reads as wrong.

### 6. `ValidatePolicyModel()` ships here

Deferred from Phase 3 because most of design section 4.8 is mask-and-type compatibility, which only
becomes checkable once masks exist. It scans every attributed type at process start and fails fast
on a type-incompatible chain, a `[DwMutate]` type that does not implement `IValueTransformer`,
`[DwDefault]` composed with another transform, and a duplicate `[DwAlias]`.

---

## Deliberately not in this phase

- **Outbound alias renaming of dynamic result columns.** Phase 3 recorded it as a Phase 4
  inheritance on the reasoning that the result transformer would own the materialized shape. It
  does — but renaming a column is not mutating a value: the generated dynamic type bakes its
  property names in at query-build time, so renaming means re-projecting into a second generated
  type, which is a different piece of work from transformation. It belongs with `/schema` in
  Phase 7, where the public vocabulary is the subject. Recorded rather than dropped.
- **`Tokenize`.** Deferred to v3.1 by the roadmap. Eight strategies ship.
- **`AllowAggregate` and `MinGroupSize`.** Phase 8, and unchanged by decision 5: this phase decides
  how a transformed value is *rendered* in a summary, not whether aggregating one is permitted.

---

## Order of operations

`ResultTransformer` runs after the pipeline returns and before the result reaches the caller.

```
query.ApplyPolicy(ctx).ToList(filter)
  1. SANITIZE          gate, inject, verify          (Phases 2-3, unchanged)
  2. AsNoTracking()                                   (Phase 2, unchanged)
  3. EXISTING PIPELINE                                (untouched)
  4. TRANSFORM         walk the graph, apply chains   [NEW]
  5. RESULT            FilterResult<T> + PolicyTrace
```

`AsNoTracking` is what makes step 4 safe, and the relationship is the reason design section 8.3
calls its test the most important in the suite: a transform applied to a *tracked* entity is
recorded by EF as a pending modification and written back as the real value on the next
`SaveChanges` anywhere in the same unit of work. Silent, irreversible, production-only.

---

## File structure

**Create in `DynamicWhere.ex/Policies/`:**

| File | Responsibility |
|---|---|
| `Enums/MaskStrategy.cs` | The eight strategies |
| `Enums/GeneralizeMode.cs`, `Enums/DatePart.cs` | Generalization |
| `Attributes/DwMaskAttribute.cs` | Masking |
| `Attributes/DwMutateAttribute.cs` | Custom transformer |
| `Attributes/DwDefaultAttribute.cs` | Replacement |
| `Attributes/DwGeneralizeAttribute.cs` | Precision reduction |
| `Attributes/DwTruncateAttribute.cs`, `Attributes/DwFormatAttribute.cs` | Text shaping |
| `Masking/IValueTransformer.cs`, `Masking/DwTransformContext.cs` | The caller's extension point |
| `Masking/MaskEngine.cs` | The eight strategies as pure functions |
| `Masking/TransformPipeline.cs` | The five stages in order |
| `Masking/MutatorCache.cs` | Compiled accessors per (Type, chain shape) |
| `Masking/GraphWalker.cs` | Direct, nested and collection paths, with a cycle guard |
| `DTOs/ValueTransform.cs` | One resolved chain |
| `Source/ResultTransformer.cs` | Applies chains to a materialized result |
| `Validation/PolicyModelValidator.cs` | `ValidatePolicyModel()` |

**Modify:** `PolicyFragment`, `FieldPolicy`, `AttributePolicyProvider`, `PolicyResolver`,
`PolicyQueryable`, `PolicyAction`, `PolicyErrorCode`, `DwPolicyOptions`.

**Create in `DynamicWhere.Tests/Policies/`:** `PolicyMaskTests.cs`, `PolicyTransformPipelineTests.cs`,
`PolicyGraphWalkTests.cs`, `PolicyValidationTests.cs`, `TrackingCorruptionTests.cs`.

---

## Tasks

- [x] **1. Enums** — `MaskStrategy` (8), `GeneralizeMode`, `DatePart`; `PolicyAction` gains
      `Mutated`, `Defaulted`, `Generalized`, added in the change that makes them emittable.
- [x] **2. The six transform attributes**, each re-declaring its `AttributeUsage` as single-use.
- [x] **3. `IValueTransformer` and `DwTransformContext`** — entity instance, field path, policy
      context, so a transformer can be role-aware.
- [x] **4. `ValueTransform` DTO and the fragment carriers** — typed, elected, following the alias
      precedent rather than `Payload`.
- [x] **5. Provider emits transform fragments**, at the level each attribute's `Overridable` implies.
- [x] **6. Resolver elects the chain**, sharing its election helper with alias and required.
- [x] **7. `MaskEngine`** — eight strategies as pure functions over a string, with the salt read from
      options.
- [x] **8. `TransformPipeline`** — the five stages, `[DwDefault]` short-circuiting, and the
      output-assignability rule enforced at the end of the chain.
- [x] **9. `MutatorCache` and `GraphWalker`** — compiled accessors, bounded depth, cycle guard.
- [x] **10. `ResultTransformer` on typed results**, direct and nested and collection paths.
- [x] **11. `ResultTransformer` on dynamic results**.
- [x] **12. Summary keys and aggregates**, with `AmbiguousGroupKey` on collision.
- [x] **13. Segment results**.
- [x] **14. Composables return the handle**, plus `AsUnguardedQueryable()`.
- [x] **15. `ValidatePolicyModel()`** and `DwPolicyOptions.HashSalt` / `ServiceProvider`.
- [x] **16. SQLite end to end**, typed and dynamic, both tiers.
- [x] **17. The tracking-corruption test** — mask, assert every entity detached, modify an unrelated
      entity in the same context, `SaveChanges`, re-read with raw SQL, assert the original value
      intact. Blocking.
- [x] **18. Phase gate** — release build with zero warnings, full suite green, the four named files
      untouched, roadmap updated.

---

## What to watch for

The recurring defect on this branch is fail-open, and this phase has two new shapes of it.

- **A path the walker fails to reach is a value returned raw.** The same class of bug as Phase 1's
  five navigation gaps, now on the output side. Arrays, interfaces, structs, custom collections and
  jagged shapes all have fixtures already; the walker must handle every one of them and terminate on
  the two cyclic shapes.
- **A transform that throws must not be swallowed.** A masking function that fails on an unexpected
  value and is caught into "return the original" hands back the real value. Every failure fails
  closed, and there is no catch-all in the pipeline.
