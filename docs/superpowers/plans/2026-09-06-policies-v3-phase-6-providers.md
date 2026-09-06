# Policies v3.0 — Phase 6: Store providers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The store stops being a thing that only exists in process memory. A rule written to Redis or to a relational database reaches the resolver as the same fragment an in-memory rule does, under the same eleven conformance tests, with nothing lost in the round trip through a wire format.

**Architecture:** Two new packages, both behind `IDwPolicyStore`. Nothing in the core changes shape: `StorePolicyProvider` already loads, pins, watches and polls, and it cannot tell the three implementations apart. What is new is **serialization** — this is the first phase in which a `PolicyRule` leaves the process — and that is where the whole security surface of this phase lives.

**Tech Stack:** .NET 6 libraries. `StackExchange.Redis` for one, `Microsoft.EntityFrameworkCore` 6.0.22 for the other. Tests on net8.0 with Testcontainers for Redis and PostgreSQL.

**Branch:** `feat/policies-v3`. **Baseline: 1072 tests passing, zero warnings.**

---

## Four decisions settled before implementation

### 1. One whole-rule serializer, and it refuses what it cannot carry

Design §5.1 gives a rule a single `Payload` column and describes its purpose as "mask spec, default
value, bucket step". `PolicyPayload` reads exactly that and no more. But the `PolicyRule` Phase 5
actually shipped carries **four more things**, none of which §5.1 has a column for:

| Carrier | What it does | What dropping it does |
|---|---|---|
| `Forced` | Adds a predicate to every query on the type | A tenant scope silently stops applying — cross-tenant disclosure |
| `RequiredOperators` | Demands the caller filter on the field | The requirement disappears; the field is readable unscoped |
| `AllowedOperators` | Restricts which operators may touch the field | `null` means "says nothing", so the restriction becomes no restriction |
| `Alias` | The public name the field answers to | Cosmetic, and the only one of the four that is |

Three of the four fail **open** when dropped, and they fail open silently: the rule still loads, the
snapshot still counts it, and the operator who wrote it sees it listed. `InMemoryPolicyStore` has
never exposed this because it holds `PolicyRule` objects and never serializes one — and **no
existing store test exercises any of the four.** This phase is the first time it matters, and the
gap is in the design document rather than in the code.

So: one `PolicyRuleDocument` in `Policies/Storage/`, the single place a whole rule is read or
written, delegating the transform half to `PolicyPayload` so that class stays the one transform
authority. Both providers map through it. It **throws** on anything it cannot represent, exactly as
`PolicyPayload` does, and for the same stated reason: a reader that returns null in this position
produces a rule that is stored, is listed, and enforces less than it says.

Three details that are each a place to fail open, and each gets its own test:

- **`Operator` is written by name, never by number.** `Operator.Equal` is `0`, so an absent or
  unparsed operator reads as an equality restriction — a real, plausible-looking restriction that
  nobody wrote. This is the same argument `PolicyPayload.ReadEnum` already makes for `MaskStrategy`
  and `TransformKind`, applied to a fourth enumeration.
- **Null and empty are different and must both survive.** For `RequiredOperators`, `null` is "no
  requirement" and `[]` is "a requirement nothing can satisfy". A serializer that writes an absent
  property for both reverses the decision on the way back in. Absent stays absent; `[]` stays `[]`.
- **`ForcedPredicate` has three mutually exclusive shapes** — constant, context-read, null-check —
  built through three factories that enforce the exclusivity. The reader reconstructs through the
  same factories rather than setting fields, so a document naming both a `Value` and a
  `ContextValue` is rejected rather than resolved by whichever the reader happens to check first.

### 2. A row that cannot be read is a load failure, not a skipped row

A malformed payload, an unknown enum name, a `SubjectKind` this version does not define: every one
of them throws out of `LoadAsync`, failing the whole load.

The alternative — skip the bad row, load the rest — is the branch's recurring shape wearing a
availability argument as a hat. A skipped row is a denial that has stopped applying, and nothing
anywhere reports it: the snapshot loads, the version advances, the instance is healthy, and one
control is gone. Failing the load routes into machinery that already exists and is already tested:
at startup it refuses to boot, and afterwards it is a refresh failure governed by `StoreFailure`,
which holds the last known good snapshot and marks the instance degraded until the ceiling escalates
it to `FailClosed`.

Both providers therefore parse **every** row on **every** load. The cost is bounded by the broad
zone, which §5.3 already bounds by the number of subjects an organisation defines.

### 3. Case-insensitive subject matching must survive the database's collation

`PolicyRule.MatchesSubject` compares identities with `OrdinalIgnoreCase`, deliberately, because
identities arrive from token claims whose casing this library does not control. The conformance
suite pins it: `A_narrow_load_matches_identities_case_insensitively`.

That guarantee does not survive a naive translation to either store:

- **Redis keys are case-sensitive, always.** A user rule written under `...:user:Alice` and fetched
  under `...:user:alice` simply is not found.
- **A relational comparison is case-sensitive or not depending on the column's collation.**
  SQL Server's default collation is case-insensitive and PostgreSQL's `=` is case-sensitive, so the
  identical query and the identical rows give different answers on two providers this one package
  claims to serve. The failure mode is a user-level denial that applies on the developer's SQL
  Server and does not apply on the production Postgres.

Both stores therefore **normalize on write**: Redis derives its per-user key from the lowercased
identity, and the EF model carries a `SubjectKeyNormalized` column that the narrow query filters on.
Matching happens on the normalized value in both, so collation and key casing decide nothing. The
original `SubjectKey` is stored unchanged, because it is what an operator typed and what `/explain`
will show them in Phase 7.

### 4. Enums are persisted by name, and the zone split is honoured in the storage layout

Phase 5's whole boundary argument is that three of the four enumerations involved default to their
most permissive member, so a row of all zeroes reads as "grant everyone everything, above sealed".
A store that persists enums as integers hands that row to anything that can write a `0` — including
a column added with a `NOT NULL DEFAULT 0`.

Persisted as names, the same absent column produces an empty string, which parses to nothing and is
refused. `PolicyFeature` is flags and round-trips through `Enum.ToString`/`TryParse` as
`"Where, Select"`; `PolicyRule`'s constructor still masks it against `All` and refuses `None`, so
the name path and the boundary check are independent of one another.

The zone split is likewise structural rather than a filter applied after loading:

- **Redis** keeps broad rules in one hash and each caller's user rules in a hash of their own, so
  `LoadAsync` is one `HGETALL` bounded by subject count and never reads a user rule at all.
- **EF** filters the narrow query by `SubjectKind = 'User'` and the broad load by `<> 'User'`,
  against the `(SubjectKind, SubjectKey, EntityType)` index §5.6 already specifies.

`StoreSnapshot` and `NarrowZone` refuse the other zone's rules at construction, so a mistake in
either layout is a thrown exception rather than a million rows or a missing denial. Neither store
filters `Enabled` itself — the zones already drop disabled rules, and one behaviour in one place is
what keeps three stores from having three answers.

---

## Deliberately not in this phase

- **The admin surface.** Phase 7. Nothing here is reachable over HTTP, and neither package
  references ASP.NET.
- **Migrations.** See below — the package ships the model and no migration.
- **The EF Core 6.0.22 CI leg.** Phase 9, per the roadmap. Both new packages target the same floor,
  so the leg will cover them when it lands.
- **`[DwMutate]` from a store.** `PolicyPayload` refuses a transformer type in both directions and
  `PolicyRuleDocument` inherits that refusal unchanged.

---

## Migrations: the package ships the model, not the migration

Design §5.6 claims one package serves SQL Server, Postgres "and any other EF-supported provider
because the store uses no raw SQL". A migration is generated per provider, so shipping one would
make that claim false the moment a third provider appeared, and would collide with a consumer whose
own migration history already owns their schema.

The package therefore ships:

- `DwPolicyRuleConfiguration` and `DwPolicyVersionConfiguration` as `IEntityTypeConfiguration<>`, so
  a consumer can call `modelBuilder.ApplyConfiguration(...)` and let the two tables join the
  migration history they already have; and
- `DwPolicyDbContext` for the standalone case, built from the same two configurations.

The store is written against `Func<DbContext>` and reaches its tables through `context.Set<T>()`, so
it works identically over the shipped context and over a consumer's own. Generating the migration is
the consumer's step, and Phase 9's documentation says so in one paragraph.

The version row is bumped **without raw SQL**: `Version` is a concurrency token, so a lost update
throws `DbUpdateConcurrencyException` and is retried, rather than two writers reading `41` and both
writing `42`. That is what the PostgreSQL container leg exists to prove — SQLite's locking would let
the naive version pass.

---

## Order of operations

```
startup
  1. store.LoadAsync()                                     Redis: HGETALL broad hash
                                                           EF:    SELECT WHERE SubjectKind <> User
       └─ every row through PolicyRuleDocument             throws on any unreadable row     [NEW]
            └─ every rule through PolicyRule's ctor        the Phase 5 boundary, unchanged
  2. StorePolicyProvider.CreateAsync(...)                  (unchanged)

per request
  3. DwPolicy.PrepareAsync(ctx)                            Redis: HGETALL per user key
                                                           EF:    SELECT WHERE Normalized IN (...)

per query
  4. SANITIZE / PIPELINE / TRANSFORM                       (Phases 2-4, unchanged)

background
  5. Redis: SUBSCRIBE dw:policy:version  ─┐
     EF:    (no watch, returns null)      ├─ both polled behind, by GetVersionAsync   (unchanged)
                                          ─┘
```

Step 4 is untouched by this phase. Nothing in the sanitizer, the transformer or the resolver knows
which store it is being served from, which is the property the conformance suite exists to keep.

---

## File structure

**Create in `DynamicWhere.ex/Policies/Storage/`:**

| File | Responsibility |
|---|---|
| `PolicyRuleDocument.cs` | The one whole-rule ↔ JSON routine, and `RuleDetail` |

**Create `DynamicWhere.ex.Policies.Redis/`:**

| File | Responsibility |
|---|---|
| `DynamicWhere.ex.Policies.Redis.csproj` | net6.0, `StackExchange.Redis`, version locked to the core |
| `RedisPolicyStore.cs` | The store: key layout, transactional writes, `INCR`, `PUBLISH` |
| `RedisPolicyKeys.cs` | The key layout in one place, including the lowercased user key |

**Create `DynamicWhere.ex.Policies.EntityFrameworkCore/`:**

| File | Responsibility |
|---|---|
| `DynamicWhere.ex.Policies.EntityFrameworkCore.csproj` | net6.0, EF Core 6.0.22, version locked to the core |
| `DwPolicyRuleRecord.cs` | The persistence POCO — never a `PolicyRule`, see below |
| `DwPolicyVersionRecord.cs` | The single-row version, `Version` as the concurrency token |
| `DwPolicyRuleConfiguration.cs` | Columns, the two §5.6 indexes, enums as names |
| `DwPolicyVersionConfiguration.cs` | The single row and its token |
| `DwPolicyDbContext.cs` | The standalone context, for consumers who want one |
| `EfPolicyStore.cs` | The store over `Func<DbContext>` |

**Modify:** `DynamicWhere.ex.sln` (both projects), `.github/workflows/publish.yml` (pack three),
`build/check-version.ps1` (check three), `DynamicWhere.Tests.csproj` (Testcontainers, Npgsql, the
two project references).

**A persistence POCO, not the rule itself.** EF materializes by setting properties on a mutable
object with a parameterless constructor, which would bypass every refusal in `PolicyRule`'s
constructor — the exact guards Phase 5 mutation-checked one at a time. `DwPolicyRuleRecord` is that
mutable object, and converting it to a `PolicyRule` runs the constructor on every row on every load.
The boundary stays where it was put.

**Create in `DynamicWhere.Tests/Policies/`:** `PolicyRuleDocumentTests.cs`, `RedisStoreTests.cs`,
`EfPolicyStoreTests.cs`, and the three conformance derivations — Redis on Testcontainers, EF on
SQLite and on Testcontainers PostgreSQL.

---

## Tasks

- [x] **1. `PolicyRuleDocument`** — whole rule to JSON and back, every carrier round-tripped,
      operators by name, null distinguished from empty, `ForcedPredicate` through its own factories,
      a document naming both a value and a context value refused, `MutateStage` refused in both
      directions, every malformed shape throwing rather than returning a partial rule.
- [x] **2. The two projects, the solution and the release plumbing** — both csprojs at the core's
      version, both added to `DynamicWhere.ex.sln`, `publish.yml` packing all three, and
      `check-version.ps1` failing when the three versions disagree. The check is extended and seen
      to fail before the projects exist.
- [x] **3. The EF model** — records, configurations, the standalone context, enums as names, the
      `SubjectKeyNormalized` column, and the two indexes from §5.6.
- [x] **4. `EfPolicyStore`** — load, narrow, version, upsert, delete, no watch, and the version bump
      as a concurrency token with a bounded retry.
- [x] **5. EF conformance** — derived twice, on SQLite and on PostgreSQL, plus the sealed-field
      resolver hook the base already supplies.
- [x] **6. `RedisPolicyStore`** — the key layout, one transaction per write covering the rule, the
      owner index, the `INCR` and the `PUBLISH`, and `GetVersionAsync` reading the version key for
      real rather than stubbing it.
- [x] **7. The Redis watch** — subscribe to `dw:policy:version`, surface it as `IAsyncEnumerable`,
      and prove the poll still bounds a dropped message.
- [x] **8. Redis conformance** — derived on Testcontainers, with a fresh key prefix per store.
- [x] **9. The reading pass** — the fail-open sweep across everything this phase added, before the
      gate. Seventeen of this shape across five phases, and not one found by a test that already
      existed.
- [x] **10. Phase gate** — release build zero warnings across the whole solution, full suite green,
      the four named files untouched, roadmap updated.

---

## What to watch for

- **Every naming axis this phase adds is a place to fail open, and it adds four**: a Redis key, a
  normalized subject column, an enum spelled as a name, and a JSON property name. Each one that
  fails to match produces a rule that does not apply, and a rule that does not apply is access
  granted. The conformance suite pins the subject axis; the other three need their own tests.
- **The round trip is the test, not the write.** Asserting that a rule was stored proves nothing
  about what comes back. Every carrier test must go store → load → resolve, and assert on the
  fragment, because that is the only place a dropped `Forced` becomes visible.
- **A test that passes either way proves nothing.** Mutation-check the three fail-open carriers:
  drop `Forced`, `AllowedOperators` and `RequiredOperators` from the document writer in turn and
  confirm the suite goes red for each. Three carriers means three checks, not one.
- **Testcontainers means the suite now needs Docker.** `publish.yml` runs the tests before it packs,
  so a container that cannot start fails a release, not just a build. ubuntu-latest supplies one;
  this is worth one line in the phase outcome so Phase 9 is not surprised by it.
- **Restore a mutated file from a copy, never `git checkout --`.** Phase 5 lost an uncommitted fix
  that way mid-mutation-check.
- **Stage with explicit pathspecs.** Never `git add -A`. Two new project directories make a
  tree-wide add more tempting and more expensive than usual.
