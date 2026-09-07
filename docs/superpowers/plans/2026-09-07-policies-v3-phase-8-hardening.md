# Phase 8 — Security hardening

**Branch:** `feat/policies-v3`
**Roadmap:** [2026-08-22-policies-v3-roadmap.md](2026-08-22-policies-v3-roadmap.md)
**Spec:** [2026-08-22-dynamicwhere-policies-design.md](../specs/2026-08-22-dynamicwhere-policies-design.md)
**Written:** 2026-09-07, against the code Phase 7 left behind.

**Exit criteria, from the roadmap:** all seven inference attacks blocked. Earlier phases carry their
own security tests inline; this phase covers only what spans features.

---

## What this phase is

The cross-cutting mitigations that needed masking and Summary support to exist first, and the
completion of `PolicyInferenceTests` so that every section of the design's threat analysis ends the
phase with a test that goes red when its mitigation is removed.

Nothing here is a new feature. Every line of it exists to close a channel through which a caller
can learn a value the policy refused them.

---

## Decisions settled before implementation

### 1. The bar is seven mitigations across five channels

§7 names five disclosure channels; the roadmap's exit line says seven. Both are right, counted
differently — §7.1 delivered two mitigations and §7.2 needs two:

| # | Channel | Mitigation | State |
|---|---|---|---|
| 1 | §7.1 | Policy applies to every segment independently | Shipped `fe5e33b` |
| 2 | §7.1 | Strict tier: deny-select ⇒ deny-where inside a `Segment` | Shipped `fe5e33b` |
| 3 | §7.5 | Strict tier refuses `getQueryString` | Shipped `fe5e33b` |
| 4 | §7.2 | Aggregating a transformed field is denied by default | **This phase** |
| 5 | §7.2 | `MinGroupSize` floor suppresses small groups | **This phase** |
| 6 | §7.3 | `[DwOperators]` is the control; `TotalCount` is a documented consequence | **Test only** |
| 7 | §7.4 | Startup warns on masked-but-orderable; `[DwNoOrder]` is the fix | **Test only** |

Six and seven have their mitigations already — §7.3's is `[DwOperators]`, shipped in Phase 1, and
§7.4's is the startup warning `PolicyModelValidator` already emits. What they lack is a test that
*reproduces the attack* and shows the control stopping it. A mitigation nobody has attacked is a
claim, not a control.

### 2. Every transform blocks aggregation, not only `[DwMask]`

§7.2 says "aggregating a masked field is denied by default". Taken literally that leaves
`[DwGeneralize]` — the attribute the spec itself recommends for the numeric fields, which is to say
exactly the fields people aggregate — with no protection at all. `MAX` over a generalized salary
returns the real maximum, because SQL aggregates run before any transform.

So the rule is: **a field whose value is transformed on output may not be aggregated**, unless the
attribute that transforms it opts in. `AllowAggregate` goes on all six transform attributes, and a
field is aggregatable only when **every** stage in its elected chain permits it — a rule adding a
truncation on top of a mask that allowed aggregation makes the field un-aggregatable again, which
is the direction that is safe to get wrong.

**Enforced in the resolver, not in the sanitizer.** `PolicyResolver` elects the transform chain
already; denying `Aggregate` there means one place, covering attributes and runtime rules alike. A
sanitizer-side check would have to be repeated at every surface that aggregates, and the standing
lesson of Phases 3 and 4 is that such a list is never finished.

### 3. The floor gets the group size by injecting a count and stripping it

A caller asking for `MAX(Salary)` has given the library no count column, so the floor has nothing
to compare against `k`. The sanitizer adds a `Count` aggregate under a reserved alias, the floor
reads it, and the column is removed before the result is returned — the same move `[DwForceWhere]`
already makes, the library adding to a query what it needs for its own reasons.

The alternatives both fail. Requiring the caller to supply a count breaks every existing report
until someone adds a column they must then hide. Applying the floor only when a count happens to be
present fails open in the exact shape this branch has found twenty-four times: omit the column and
the guard does nothing while the configuration says it is in force.

A caller who has already used the reserved alias is refused rather than silently overwritten.

### 4. A group under the floor is dropped, and the drop is recorded

§7.2 says "suppresses groups smaller than k". Refusing the whole summary instead would let one
department of one kill a company-wide report, which on real data means the control gets turned off.
Every suppression is written to the trace, so a missing group is answerable rather than mysterious.

**The floor is off by default.** `MinGroupSize` defaults to 1, so no existing caller changes
behaviour; set above 1 it applies to every grouped summary, not only those aggregating a protected
field. A group of one is a re-identification risk whatever is in it, and making the floor
conditional on a transform would leave an unmasked-but-sensitive field with no floor at all.

A **per-field override** rides on the transform attributes beside `AllowAggregate`, as the
roadmap's decision table promised. The effective floor is the largest of the global setting and
every aggregated field's own.

---

## What the code already provides, and what it does not

Reviewed against the tree at `273889d`.

| Needed | State |
|---|---|
| `PolicyInferenceTests` | Exists, 11 tests, channels 1–3 closed and mutation-verified. |
| §7.4's warning | **Already shipped.** `PolicyModelValidator.CheckMaskedButOrderable`. Needs an attack test, not code. |
| §7.3's control | **Already shipped.** `[DwOperators]` since Phase 1. Needs an attack test, not code. |
| `[DwMask].AllowAggregate` | Does not exist. Nor on the other five transform attributes. |
| `ValueTransform` | Composes six stages; has no notion of aggregation or of a floor. |
| `DwPolicyOptions.MinGroupSize` | Does not exist. §7.2 names `options.MinGroupSize`; the roadmap adds "with a per-field attribute override". |
| `PolicyErrorCode.GroupTooSmall` | Does not exist. §6.3 reserves the name. Appended at 20. |
| Summary pipeline | `FilterSanitizer.Sanitize<T>(Summary)` produces the clause; `ResultTransformer.Summary` runs after materialization. The floor needs both halves. |
| Paging | Happens **in SQL**, before the library sees a row. See the limitation below. |

**The floor cannot see past the current page.** The database pages and computes `TotalCount` before
suppression runs, so a page can come back short and the total will have counted groups that were
dropped. Nothing protected leaks; the numbers simply do not reconcile. Fixing it means moving the
floor into the generated SQL, which is the pipeline this phase must not touch.

---

## Tasks

Each ends with a commit. TDD throughout: the failing test is written and seen to fail first.

- [ ] **1. Plan.** This document.
- [ ] **2. Aggregation is denied on a transformed field.** `AllowAggregate` and `MinGroupSize` on
      the six transform attributes; carried on `TransformStage`; composed onto `ValueTransform`
      (`AllowsAggregate` as AND, `MinGroupSize` as MAX); `PolicyResolver` denies `Aggregate` when
      the elected chain does not permit it. Mutation-checked.
- [ ] **3. The group-size floor.** `DwCaps.MinGroupSize`; `PolicyErrorCode.GroupTooSmall`; the
      injected count and its reserved alias; suppression in `ResultTransformer`; every drop in the
      trace; the per-field override composed with the global setting.
- [ ] **4. The security regression suite reaches seven.** `PolicyInferenceTests` gains §7.2's two
      attacks, §7.3's cardinality attack against `[DwOperators]`, and §7.4's binary search against
      the startup warning — each reproducing the attack, each red when its mitigation is removed.
- [ ] **5. The reading pass.** Twenty-four fail-open defects across seven phases, none found by a
      test that already existed. Read the diff for the twenty-fifth.
- [ ] **6. Close.** Roadmap outcome, memory.

---

## Standing rules that bind this phase

- No edits to `Builder.cs`, `Validator.cs`, `Converter.cs`, `Normalizer.cs`. Verified before close.
- Explicit pathspecs when staging. Never `git add -A`.
- Nothing pushed. Merging to master publishes to NuGet and redeploys the docs site.
- Mutation-check anything security-relevant: break it deliberately, confirm the suite goes red.
  This phase is *entirely* security-relevant, so every mitigation gets one.
