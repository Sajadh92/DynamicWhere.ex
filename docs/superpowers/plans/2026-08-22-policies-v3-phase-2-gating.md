# Policies v3.0 — Phase 2: Gating — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make policy decisions actually affect a query. Add the `ApplyPolicy(ctx)` handle, a pure `FilterSanitizer` that rewrites or rejects a `Filter` before it reaches the untouched pipeline, and `AsNoTracking` on the guarded path.

**Architecture:** `ApplyPolicy(ctx)` returns a `PolicyQueryable<T>` carrying the caller and a resolver. Each of its methods clones the incoming `Filter`, canonicalizes every field path through the library's own `Validate<T>()`, resolves a `FieldPolicy` per path, gates each feature, and hands the sanitized clone to the existing extension method unchanged. Enforcement is a pre-filter; the pipeline never learns policy exists.

**Tech Stack:** .NET 6 library, xUnit on net8.0, EF Core SQLite for integration. No new package references.

**Branch:** `feat/policies-v3`. **Baseline: 516 tests passing, zero warnings.**

---

## Two decisions taken during planning

### Canonicalize through `Validate<T>()`, not alongside it

`Validator.Validate<T>(this string name)` delegates to `CacheReflection.ValidatePropertyPath`, which splits on `.` with `RemoveEmptyEntries | TrimEntries` and rebuilds the path from CLR property names. That is the canonical form the rest of the library uses, and it runs *inside* the pipeline — after the sanitizer.

Phase 1 closed the resulting divergence by giving `PolicyFragment.NormalizePath` matching behaviour, but two normalizers that agree by construction is a standing liability: the next person to touch either one has no way to know they are paired. The sanitizer therefore calls `Validate<T>()` itself, on its clone, before resolving any policy. One canonical form, produced by one function.

Consequence worth knowing: `Validate<T>()` throws on an unknown field, so an invalid path now fails as a validation error before any policy decision is reached. That is the right order — a field that does not exist has no policy — but it means a caller probing for field existence gets the same error guarded or unguarded, which is correct and non-disclosing.

### Clone deeply, and accept that this diverges from the unguarded path

`Validator` mutates what it validates: `condition.Field = condition.Field.Validate<T>()`, and likewise for `OrderBy`, `GroupBy`, and `AggregateBy`. Today an unguarded `ToList(filter)` rewrites the caller's own `Filter` in place.

The sanitizer clones first, so a guarded query leaves the caller's object untouched. That is a deliberate behavioural difference between the two paths and an improvement on both counts — callers reuse filter objects across queries, and a policy-modified filter must never leak back to the caller. Document it; do not "fix" the unguarded path, which is out of scope and would be a breaking change.

`ConditionGroup` is recursive through `SubConditionGroups`, so the clone is a deep one and every tree walk in this phase must recurse.

---

## Three decisions taken during review

The draft above was reviewed against the code on 2026-08-26 before execution. Three items were
wrong or missing; all three are settled here and the affected tasks below are rewritten to match.

### The phase gate checks four files, not the `Source/` directory

`DynamicWhere.ex/Source/` holds exactly five files: the four this project must never touch, plus
`Extention.cs`, which defines the `Extension` class. Task 13 puts the `RequirePolicy` check in
`Extension` -- it has to live where the *unguarded* path can see it -- so the directory-wide gate in
Task 17 and Task 13 could not both hold. The directory check was always an over-approximation of
the roadmap's actual standing rule, which names four files and has never named `Extention.cs`. It
passed until now only because nothing had touched the fifth file yet.

Task 17's gate is therefore narrowed to the four named files. `Extention.cs` gains exactly one
guard clause and is reviewed on its own.

### `Summary` carries two field vocabularies and only one of them is a property path

`GroupBy.Fields` and `AggregateBy.Field` are property paths. `Summary.Having`'s condition fields
and `Summary.Orders`'s fields are **aggregate aliases** -- `Validator.ValidateHavingCondition`
checks `validAliases.Contains(condition.Field)` and never touches reflection, and
`Validate<T>(this Summary)` accepts an order field matching a group-by field *or* an alias.

Running such a field through `Validate<T>()` throws `LogicException` on a query that is valid
today. Skipping it instead opens the fail-open cousin: `AggregateBy { Field = "Salary", Alias =
"S" }` followed by `Having S > 100000` reads a deny-aggregate field through its alias.

Neither this plan's draft nor spec section 6.2 mentions `Having` at all. The Summary overload
therefore builds the alias-to-source-field map first, and gates each aliased reference against the
policy of the field it aggregates rather than against a path lookup that would throw.

### Deny-select is enforced by synthesizing a projection

`Extension.ToList` projects only `if (filter.Selects != null)`. A caller who sends no `Selects`
gets the whole entity with every denied column populated, so gating a projection list alone leaves
`[DwNoSelect]` bypassable by omission -- which is the phase's own recurring fail-open shape,
reached by doing less rather than more.

When `Selects` is null *and* the resolved policy denies `Select` on any field of `T`, the sanitizer
builds the projection from the allowed fields instead of leaving it null. A caller who denies
nothing still gets `Selects == null` and the byte-identical unguarded path.

---

## File structure

**Create in `DynamicWhere.ex/Policies/`:**

| File | Responsibility |
|---|---|
| `Enums/PolicyErrorCode.cs` | The closed set of policy refusal codes |
| `Enums/PolicyAction.cs` | What a decision did: Allowed, Denied, Dropped, … |
| `DTOs/PolicyDecision.cs` | One recorded decision |
| `DTOs/PolicyTrace.cs` | The decisions for one query |
| `Attributes/DwOperatorsAttribute.cs` | Allowed / denied operators per field |
| `Source/FilterSanitizer.cs` | Pure `Filter in → Filter out`. The whole gate |
| `Source/PolicyQueryable.cs` | The handle returned by `ApplyPolicy` |
| `Source/PolicyExtensions.cs` | `ApplyPolicy(ctx)` and the guard check |

**Modify:**

- `DynamicWhere.ex/Exceptions/PolicyException.cs` — carry `PolicyErrorCode`
- `DynamicWhere.ex/Classes/Complex/Filter.cs` — add an internal deep `Clone()`
- `DynamicWhere.ex/Classes/Core/ConditionGroup.cs` — add an internal deep `Clone()`

**Create in `DynamicWhere.Tests/Policies/`:**

| File | Responsibility |
|---|---|
| `FilterSanitizerTests.cs` | The gate, as a pure function. No database |
| `PolicyTraceTests.cs` | Decision recording |
| `GuardedQueryTests.cs` | End-to-end against the SQLite fixture |

---

## Task 1: PolicyErrorCode

Inherited decision from Phase 1: settle `PolicyException.Code`'s type before any library code throws one. `ErrorCode` stays internal — it holds ~30 members, most unrelated to policy, and publishing all of them to expose nine would commit the rest as API surface permanently.

**Files:**
- Create: `DynamicWhere.ex/Policies/Enums/PolicyErrorCode.cs`
- Modify: `DynamicWhere.ex/Exceptions/PolicyException.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PolicyExceptionTests.cs`:

```csharp
    [Fact]
    public void A_policy_exception_carries_a_typed_code_a_caller_can_switch_on()
    {
        PolicyException exception = new(PolicyErrorCode.FieldDeniedForWhere, "Salary", PolicyFeature.Where, DwTier.Strict);

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, exception.ErrorCode);
        Assert.Equal("FieldDeniedForWhere", exception.Code);
    }

    [Fact]
    public void Every_error_code_has_a_distinct_name_usable_as_the_string_code()
    {
        PolicyErrorCode[] codes = Enum.GetValues<PolicyErrorCode>();

        Assert.Equal(codes.Length, codes.Select(c => c.ToString()).Distinct().Count());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyExceptionTests"`
Expected: FAIL to compile — `PolicyErrorCode` does not exist.

- [ ] **Step 3: Write the implementation**

Create `DynamicWhere.ex/Policies/Enums/PolicyErrorCode.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// The closed set of reasons a policy refuses part of a query.
/// </summary>
/// <remarks>
/// Typed rather than a bare string so a caller can switch exhaustively with compiler help:
/// <c>catch (PolicyException ex) when (ex.ErrorCode == PolicyErrorCode.FieldDeniedForWhere)</c>
/// binds to a symbol the library can rename safely, where a string literal would silently stop
/// matching. <c>PolicyException.Code</c> keeps the string form for logging and serialization.
/// </remarks>
public enum PolicyErrorCode
{
    /// <summary>Filtering on the requested field is refused.</summary>
    FieldDeniedForWhere = 1,

    /// <summary>Projecting the requested field is refused.</summary>
    FieldDeniedForSelect = 2,

    /// <summary>Sorting by the requested field is refused.</summary>
    FieldDeniedForOrder = 3,

    /// <summary>Grouping by the requested field is refused.</summary>
    FieldDeniedForGroup = 4,

    /// <summary>Aggregating the requested field is refused.</summary>
    FieldDeniedForAggregate = 5,

    /// <summary>The requested field is refused inside a set operation.</summary>
    FieldDeniedForSegment = 6,

    /// <summary>Every requested projection field was refused, leaving no projection.</summary>
    AllSelectsDenied = 7,

    /// <summary>The requested operator is not permitted on this field.</summary>
    OperatorNotAllowed = 8,

    /// <summary>The query exceeded a configured cap.</summary>
    CapExceeded = 9,

    /// <summary>The entity requires a policy context and the query supplied none.</summary>
    PolicyRequired = 10
}
```

Change `PolicyException`'s constructor to take `PolicyErrorCode`, expose it as `ErrorCode`, and derive the string `Code` from `errorCode.ToString()`. Keep every other member as it is.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyExceptionTests"`
Expected: PASS. The three pre-existing tests need their construction updated to the new signature — that is expected and permitted for this task only.

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Enums/PolicyErrorCode.cs DynamicWhere.ex/Exceptions/PolicyException.cs DynamicWhere.Tests/Policies/PolicyExceptionTests.cs
git commit -m "feat(policies): give PolicyException a typed error code"
```

---

## Task 2: PolicyAction and PolicyDecision

**Files:**
- Create: `DynamicWhere.ex/Policies/Enums/PolicyAction.cs`
- Create: `DynamicWhere.ex/Policies/DTOs/PolicyDecision.cs`
- Create: `DynamicWhere.Tests/Policies/PolicyTraceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `PolicyTraceTests.cs`:

```csharp
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the decision record the sanitizer emits, which is the only way a caller can tell why a
/// field is missing from a result.
/// </summary>
public class PolicyTraceTests
{
    [Fact]
    public void A_decision_records_what_happened_to_which_field_and_why()
    {
        PolicyDecision decision = new("Salary", PolicyFeature.Select, PolicyAction.Dropped, "DwNoSelectAttribute");

        Assert.Equal("Salary", decision.FieldPath);
        Assert.Equal(PolicyFeature.Select, decision.Feature);
        Assert.Equal(PolicyAction.Dropped, decision.Action);
        Assert.Equal("DwNoSelectAttribute", decision.Reason);
    }

    [Fact]
    public void A_decision_requires_a_field_path()
    {
        Assert.Throws<ArgumentException>(() =>
            new PolicyDecision(" ", PolicyFeature.Select, PolicyAction.Dropped, "why"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyTraceTests"`
Expected: FAIL to compile — `PolicyAction` and `PolicyDecision` do not exist.

- [ ] **Step 3: Write the implementation**

Create `DynamicWhere.ex/Policies/Enums/PolicyAction.cs`:

```csharp
namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// What a policy decision did to one feature of one field.
/// </summary>
public enum PolicyAction
{
    /// <summary>The request proceeded untouched.</summary>
    Allowed = 0,

    /// <summary>The request was refused and the query threw.</summary>
    Denied = 1,

    /// <summary>The request was silently removed from the query.</summary>
    Dropped = 2,

    /// <summary>The request proceeded and the output value will be transformed.</summary>
    Masked = 3,

    /// <summary>A predicate was added to the query that the caller did not send.</summary>
    Injected = 4
}
```

Create `DynamicWhere.ex/Policies/DTOs/PolicyDecision.cs` — an immutable record of `FieldPath`, `Feature`, `Action`, `Reason`, rejecting a blank path, with XML docs on every member.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DynamicWhere.Tests --filter "FullyQualifiedName~PolicyTraceTests"`
Expected: PASS, 2 tests

- [ ] **Step 5: Commit**

```bash
git add DynamicWhere.ex/Policies/Enums/PolicyAction.cs DynamicWhere.ex/Policies/DTOs/PolicyDecision.cs DynamicWhere.Tests/Policies/PolicyTraceTests.cs
git commit -m "feat(policies): add the policy decision record"
```

---

## Task 3: PolicyTrace

**Files:**
- Create: `DynamicWhere.ex/Policies/DTOs/PolicyTrace.cs`
- Modify: `DynamicWhere.Tests/Policies/PolicyTraceTests.cs`

Holds `Tier`, `DryRun`, and a `Decisions` list, with an `Add` that appends. Needs a test that the decision list is exposed read-only, so a consumer cannot rewrite the audit record of their own query.

**Commit:** `feat(policies): add PolicyTrace`

---

## Task 4: Deep clone for Filter and ConditionGroup

The sanitizer must not mutate the caller's object, and `Validator` rewrites `Field` in place on whatever it is given.

**Files:**
- Modify: `DynamicWhere.ex/Classes/Complex/Filter.cs`
- Modify: `DynamicWhere.ex/Classes/Complex/Summary.cs`
- Modify: `DynamicWhere.ex/Classes/Complex/Segment.cs`
- Modify: `DynamicWhere.ex/Classes/Core/ConditionGroup.cs`
- Modify: `DynamicWhere.ex/Classes/Core/ConditionSet.cs`
- Modify: `DynamicWhere.ex/Classes/Core/Condition.cs`
- Modify: `DynamicWhere.ex/Classes/Core/OrderBy.cs`
- Modify: `DynamicWhere.ex/Classes/Core/GroupBy.cs`
- Modify: `DynamicWhere.ex/Classes/Core/AggregateBy.cs`
- Modify: `DynamicWhere.ex/Classes/Core/PageBy.cs`
- Create: `DynamicWhere.Tests/Policies/FilterCloneTests.cs`

Add an `internal Clone()` to every one of them. The draft named only `Filter` and `ConditionGroup`,
but Task 9 sanitizes a `Summary` and Task 14 sanitizes a `Segment`, and both reach the same leaf
types -- cloning the whole reachable graph once here is cheaper than discovering each missing
branch from a mutation bug three tasks later.

`ConditionGroup` recurses through `SubConditionGroups`; `Condition.Values` is a `List<object>` and
must be a new list, though the values themselves are shared by reference (they are scalars from
JSON). `Summary` reaches `ConditionGroup` twice -- through `ConditionGroup` and through `Having` --
and both must be cloned. `Segment` reaches it once per `ConditionSet`.

Tests must prove: mutating the clone's nested `SubConditionGroups[0].Conditions[0].Field` leaves
the original unchanged, and the same for `Selects`, `Orders`, `Page`, `Summary.Having`, and
`Segment.ConditionSets`.

**These are the only edits to `Classes/` in the whole phase.** They add a member; they change no existing behaviour.

**Commit:** `feat(policies): add deep clone to Filter and ConditionGroup`

---

## Task 5: FilterSanitizer — canonicalize and pass through

The sanitizer's skeleton: clone, canonicalize every field path through `Validate<T>()`, return. No gating yet.

**Files:**
- Create: `DynamicWhere.ex/Policies/Source/FilterSanitizer.cs`
- Create: `DynamicWhere.Tests/Policies/FilterSanitizerTests.cs`

Signature:

```csharp
internal static Filter Sanitize<T>(Filter filter, PolicyResolver resolver, DwPolicyContext context, DwPolicyOptions options, PolicyTrace trace) where T : class
```

Tests must prove the caller's `Filter` is untouched, that `"  Name  "` in a `Select` comes back canonical, that a nested `SubConditionGroups` condition is canonicalized too, and that an unknown field throws the existing validation error rather than a policy error.

**Commit:** `feat(policies): add FilterSanitizer with path canonicalization`

---

## Task 6: Gate SELECT

Drop denied fields in the convenience tier, throw in strict. If every requested field is dropped, throw `AllSelectsDenied` in both tiers — falling through to "select everything" would invert the policy's intent.

Record a `PolicyDecision` for each drop.

**Commit:** `feat(policies): gate projection fields`

---

## Task 6b: Synthesize a projection when the caller sends none

Settled in review. Gating the `Selects` list alone enforces deny-select only against callers who
volunteer one; `Extension.ToList` projects only `if (filter.Selects != null)`, so omitting it
returns the whole entity with denied columns populated.

When `Selects` is null **and** the resolved policy denies `Select` on at least one field of `T`,
replace it with the list of allowed top-level fields. When nothing is denied, leave it null so the
generated query stays byte-identical to the unguarded path -- this is the difference between a
policy layer that is invisible when unused and one that rewrites every query in the application.

Enumerate candidate fields through `CacheReflection`, the same source `Validate<T>()` reads, so the
synthesized list cannot drift from what the pipeline accepts.

Tests must prove: a denied field is absent from the result when no `Selects` was sent; a type with
no denials produces a null `Selects` and the identical SQL; and denying every field throws
`AllSelectsDenied` rather than projecting nothing.

**Commit:** `feat(policies): synthesize a projection when select is denied`

---

## Task 7: Gate ORDER

Drop denied fields in convenience, throw in strict. Unlike SELECT, an empty `Orders` list after dropping is fine — the query simply returns unordered.

**Commit:** `feat(policies): gate order fields`

---

## Task 8: Gate WHERE

**Throws in both tiers.** Dropping a filter widens the result set, so a dropped `TenantId` predicate returns every tenant's rows. This is the asymmetry the whole design rests on and the test names should say so.

Must recurse through `SubConditionGroups`. Must gate every condition, not stop at the first.

**Commit:** `feat(policies): gate filter conditions`

---

## Task 9: Gate GROUP and AGGREGATE

Both throw in both tiers. `Summary` carries `GroupBy` and `AggregateBy`; the sanitizer needs a `Summary` overload alongside the `Filter` one.

Only `GroupBy.Fields` and `AggregateBy.Field` are property paths and only they go through
`Validate<T>()`. `Summary.Having` and `Summary.Orders` are handled in Task 9b.

**Commit:** `feat(policies): gate grouping and aggregation`

---

## Task 9b: Gate Having and Summary orders through their aliases

Settled in review. `Summary.Having`'s condition fields, and any `Summary.Orders` field naming an
aggregate rather than a group-by field, are **aggregate aliases**, not property paths.
`Validate<T>()` throws on them, and skipping them lets an alias read a field whose underlying
policy denies aggregation.

Build the alias-to-source-field map from `summary.GroupBy.AggregateBy` first. Then, for each
`Having` condition and each `Summary.Orders` entry:

- If the field matches an alias, resolve the policy of that alias's `AggregateBy.Field` and gate
  the reference against it. An alias over a `Count` with no field has no underlying field and is
  allowed.
- If the field matches a group-by field, gate it as the property path it is.
- Otherwise leave it untouched and let `Validate<T>(this Summary)` reject it downstream, unchanged.

Alias comparison is `OrdinalIgnoreCase`, matching `Validator`'s own `validAliases` set. A mismatch
here is a fragment that fails to match, which is access granted.

Tests must prove: a `Having` clause over an alias whose source field is deny-aggregate throws; a
`Having` clause over an allowed alias survives untouched; a `Summary.Orders` entry naming an alias
is not sent through `Validate<T>()`; and a `Count` alias with no source field is allowed.

**Commit:** `feat(policies): gate having and summary orders through aliases`

---

## Task 10: DwOperators

**Files:**
- Create: `DynamicWhere.ex/Policies/Attributes/DwOperatorsAttribute.cs`
- Modify: `AttributePolicyProvider`, `FieldPolicy`, `PolicyFragment`, `FilterSanitizer`

`[DwOperators(Allow = new[]{ Operator.Equal, Operator.In })]` and `[DwOperators(Deny = new[]{ Operator.Contains })]`. Throws `OperatorNotAllowed`.

This is the control that stops enumeration: allowing `Equal` on a national ID while forbidding `Contains` prevents an attacker binary-searching the column one character at a time. Say that in the attribute's `<remarks>`.

`FieldPolicy` gains `AllowedOperators`; the resolver must carry it through. Note this is the first fragment payload that is not just an effect — check whether `PolicyFragment.Payload` is the right carrier or whether the fragment needs a typed field, and report the reasoning before implementing.

**Commit:** `feat(policies): enforce per-field operator restrictions`

---

## Task 11: Caps

`MaxConditions` counted across the **entire** nested group tree, `MaxOrderFields`, `MaxPageSize`, `MaxNavigationDepth` measured in path segments. All throw `CapExceeded` in both tiers.

The library has no caps today, so this closes an existing unbounded-join surface independent of access control.

**Commit:** `feat(policies): enforce query caps`

---

## Task 12: PolicyQueryable and ApplyPolicy

**Files:**
- Create: `DynamicWhere.ex/Policies/Source/PolicyQueryable.cs`
- Create: `DynamicWhere.ex/Policies/Source/PolicyExtensions.cs`

```csharp
query.ApplyPolicy(ctx).ToList(filter)
```

`PolicyQueryable<T>` holds the source `IQueryable<T>`, the context, the resolver, and the options. Its `ToList` applies `AsNoTracking()`, sanitizes, delegates to the existing extension method, and attaches the trace.

Start with `ToList` and `ToListAsync` only. The remaining 15 methods land in Task 14 once the shape is proven.

**Commit:** `feat(policies): add the ApplyPolicy handle`

---

## Task 13: DwEntity(RequirePolicy) enforcement

An unguarded `ToList(filter)` on a type marked `[DwEntity(RequirePolicy = true)]` throws `PolicyRequired`.

Without this, every field policy on a type is bypassed by simply not calling `ApplyPolicy` — this is what makes an opt-in handle a boundary rather than a suggestion.

The check has to live where the unguarded path can see it, which means the existing `Extension` class. **This is the one place in the phase where a file outside `Policies/` gains behaviour** — it is not one of the four forbidden files, but flag the diff clearly.

**Commit:** `feat(policies): enforce RequirePolicy on unguarded queries`

---

## Task 14: The remaining methods

Extend `PolicyQueryable<T>` to the other 15: `Select`, `Where`, `Order`, `Page`, `Group`, `Filter`, `Summary`, `Segment`, `ToListDynamic`, `ToListAsyncDynamic`, and the `IEnumerable` overloads.

**Commit:** `feat(policies): cover the full queryable surface`

---

## Task 15: Dry run

`options.DryRun` and `context.DryRun` both suppress every throw and drop; decisions are still recorded. Per-context is what lets one canary subject run unenforced while everyone else is enforced.

Test that dry-run output is byte-identical to unguarded output while the trace is fully populated.

**Commit:** `feat(policies): add dry-run mode`

---

## Task 16: End-to-end integration

**Files:**
- Create: `DynamicWhere.Tests/Policies/GuardedQueryTests.cs`

Against the existing SQLite `SalesFixture`, with a decorated fixture entity. Must cover: a denied field dropped from projection, a denied filter throwing, both tiers, a cap rejecting, `RequirePolicy` throwing unguarded, entities returned `Detached`, and the caller's `Filter` unmutated.

**Commit:** `test(policies): end-to-end guarded queries`

---

## Task 17: Phase gate

- `dotnet test` green, `dotnet build -c Release` at zero warnings
- The four-file gate -- **must still be empty**:

```bash
git diff --name-only master...HEAD -- DynamicWhere.ex/Source/Builder.cs DynamicWhere.ex/Source/Validator.cs DynamicWhere.ex/Source/Converter.cs DynamicWhere.ex/Source/Normalizer.cs
```

- `git diff --stat master...HEAD -- DynamicWhere.ex/Source/Extention.cs` -- expected to show the
  Task 13 guard clause and nothing else. Read the diff; do not just count lines.
- Nothing pushed
- Append `## Phase 2 outcome` to the roadmap
- Record what Phase 3 inherits

---

## Standing rules

- One implementing agent at a time. The git index is process-wide.
- Explicit pathspecs when staging. Never `git add -A`.
- No edits to `Builder.cs`, `Validator.cs`, `Converter.cs`, `Normalizer.cs`. `Extention.cs` takes
  the Task 13 guard clause and nothing else.
- The unguarded path stays byte-identical in behaviour to v2.1.5, except for the `RequirePolicy` check in Task 13.
- Every task ends green.
