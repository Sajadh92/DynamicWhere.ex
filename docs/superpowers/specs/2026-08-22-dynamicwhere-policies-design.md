# DynamicWhere.ex Policies — Design

**Status:** Approved design, ready for implementation planning
**Date:** 2026-08-22
**Target:** DynamicWhere.ex v3.0
**Current version:** 2.1.5

> **Seven corrections, recorded 2026-09-08 during Phase 9.** This document is the record of what was
> decided on 2026-08-22, not a description of what shipped. Where building against it proved it
> wrong, the correction is footnoted inline at the section it belongs to rather than edited into the
> prose — rewriting a spec to match its own implementation loses the fact that the code moved.
> The seven are in §3.4, §5.1, §5.2, §5.7, §7 (heading), §7.2 and §8.3.

---

## 1. Problem

DynamicWhere.ex accepts a JSON `Filter` from any caller and turns it into an EF Core query. The caller decides which fields to filter on, sort by, select, group by, and aggregate. The library has no concept of a field being off-limits, of a value needing to be hidden, or of who is asking.

That is fine when the caller is trusted. It is not fine when the same endpoint serves users with different entitlements, when a column holds regulated data, or when a query must be scoped to a tenant.

This design adds a policy layer: declarative rules that constrain what a caller may do with each field, enforced inside the library, configurable at compile time and at runtime.

---

## 2. Decisions

Six forks were resolved during design. Each entry records what was chosen and why, so the reasoning survives the implementation.

### 2.1 Threat model — tiered

Two postures ship in one library.

- **Convenience tier** — caller is the project's own front end. Blocked sorts and selects are dropped quietly; blocked filters still throw.
- **Strict tier** — caller is hostile or semi-trusted. Everything throws, deny-by-default is available, audit is on.

Tier is set once at startup through `DwPolicyOptions` and read from a single immutable options object at every decision point. It is never a per-call parameter, because a security posture a developer can forget to pass is not a security posture.

### 2.2 Entry point — scoped fluent handle

```csharp
query.ApplyPolicy(ctx).ToList(filter);   // policy-enforced
query.ToList(filter);                  // legacy, byte-identical to v2.1.5
```

Rejected alternatives:

- *Ambient context* (`AsyncLocal` / `IHttpContextAccessor`) — zero API change, but loses the principal in background jobs and worker services, failing open silently.
- *Explicit parameter on every method* — honest, but the library has 17 public methods, most already carrying an optional `getQueryString`. Overload count becomes unmanageable.

The handle wins because it is explicit and greppable at every call site, adds no overloads, and carries the resolved policy snapshot so one query resolves policy once.

The method was called `AsGuarded` while this document was being written; the name was settled as `ApplyPolicy` before implementation and has been updated throughout. The decision recorded here is the *shape* — a scoped handle rather than ambient context or a per-method parameter — which the rename does not affect.

### 2.3 Masking mechanism — detach, then transform in memory

`ApplyPolicy` applies `AsNoTracking()`. Masking runs after materialization, mutating detached objects.

The alternative rejected outright was masking tracked entities in place. EF Core records the mask as a pending modification; the next `SaveChanges()` anywhere in the same unit of work persists the mask as the real value. Silent, irreversible, production-only.

Projection-time masking (building the mask into the `Select` expression) was considered and rejected as the primary mechanism: it cannot express regex, salted hashes, or custom transformers, and it cannot mask a `decimal` into a string. It may be revisited later as an optimization for the subset of strategies that translate to SQL.

Detaching also means filtering and sorting still operate on **real** values in SQL while output is masked — a support agent can sort by salary band without ever seeing a number.

### 2.4 Static vs dynamic authority — opt-in ceiling

Attributes are sealed by default. `Overridable = true` opts a field into runtime lifting.

```csharp
[DwMask(MaskStrategy.Partial, Overridable = true)]  // Admin role may unmask
public decimal Salary { get; set; }

[DwDenied]                                          // nothing at runtime can grant this
public string NationalId { get; set; }
```

Rejected alternatives:

- *Hard floor* (dynamic may only tighten) — makes the "Admin sees unmasked" use case impossible, and pushes all protection into a database table where no developer reading the entity can see it.
- *Dynamic always wins* — turns the policy store into a credential. One bad migration, one SQL injection, one rogue admin row, and every protected field is exposed.

The ceiling model gives both: the headline use case works, and hard denials remain guarantees that no runtime configuration can revoke.

### 2.5 Blocked-action semantics

| Caller asked for | Convenience | Strict | Rationale |
|---|---|---|---|
| WHERE on blocked field | **Throw** | **Throw** | Dropping a filter widens the result set |
| WHERE with forbidden operator | Throw | Throw | Enumeration attempt, fail loudly |
| ORDER on blocked field | Drop | Throw | Changes row order, never row set |
| SELECT blocked field | Drop | Throw | Narrows output, safe to drop |
| GROUP BY blocked field | Throw | Throw | Leaks distribution, identifies singletons |
| AGGREGATE blocked field | Throw | Throw | Same disclosure class as select |
| `[DwRequireWhere]` missing | Throw | Throw | Missing tenant scope is never acceptable |
| Over cost/page/depth cap | Throw | Throw | Explicit limit, explicit rejection |

Sub-rules:

- **All selects dropped → throw.** Falling through to "select everything" would invert the policy's intent.
- **ConditionGroup cannot empty out.** WHERE blocks throw; the group is either intact or the request died.
- **Masked fields remain filterable and sortable.** Deliberate. The control for a field that must not be range-queried is `[DwOperators]`, not the mask.
- **Every throw carries structured data** — field path, feature, policy source, rule id, tier.
- **Dry-run overrides the whole table.** Nothing throws or drops; everything is recorded to `PolicyTrace`.

### 2.6 Release slicing — single release

All features ship in v3.0. Validation comes from the project's own test suite (`DynamicWhere.Tests` + `DynamicWhere.API`), not from staged exposure to production users.

---

## 3. Architecture

### 3.1 Sandwich, do not modify

```
query.ApplyPolicy(ctx).ToList(filter)
  |
  +-- 1. RESOLVE     capture snapshot reference, capture context
  +-- 2. SANITIZE    clone Filter; rewrite, reject, inject          [NEW]
  +-- 3. AsNoTracking()
  |
  +-- 4. ===== EXISTING PIPELINE, UNCHANGED =====
  |          Validator -> Builder -> Converter -> SQL
  |
  +-- 5. TRANSFORM   mask / mutate / default on detached graph      [NEW]
  +-- 6. RESULT      FilterResult<T> + PolicyTrace
```

Step 2 hands the untouched pipeline a sanitized `Filter`. `Builder.cs`, `Validator.cs`, `Converter.cs`, and `Normalizer.cs` receive **zero edits**. The 8,178 existing lines are unchanged; the feature is a pre-filter and a post-filter around them.

Consequences: regression risk on existing behaviour is near zero, the unguarded path is literally the v2.1.5 code path, and sanitization is unit-testable as a pure `Filter in -> Filter out` function with no database involved.

The caller's `Filter` object is cloned, never mutated — callers reuse filter instances across queries.

### 3.2 Packages

| Package | Contents | New dependencies |
|---|---|---|
| `DynamicWhere.ex` | Engine, all attributes, InMemory store, masking | `DI.Abstractions` (already transitive via EF Core) |
| `DynamicWhere.ex.Policies.Redis` | Redis store, pub/sub invalidation | `StackExchange.Redis` |
| `DynamicWhere.ex.Policies.EntityFrameworkCore` | DB store — SQL Server and Postgres both | EF Core only |
| `DynamicWhere.ex.Policies.AspNetCore` | Admin API, explain endpoint, `ClaimsPrincipal` adapter | ASP.NET Core |

The core package stays dependency-clean. Consumers who never call `ApplyPolicy` pull nothing new. One EF Core package covers SQL Server and Postgres because the store uses no raw SQL.

### 3.3 Folder layout

Mirrors the existing `Optimization/Cache` subsystem precedent.

```
Policies/
  Attributes/     DwDeniedAttribute, DwMaskAttribute, ... (24)
  Config/         DwPolicyOptions, DwCaps
  Context/        DwPolicyContext, DwSubject
  DTOs/           FieldPolicy, PolicyFragment, PolicySource, PolicySnapshot, PolicyTrace, PolicyDecision
  Enums/          DwSubjectKind, DwTier, MaskStrategy, PolicyEffect, PolicyFeature, PolicyLevel
  Masking/        IValueTransformer, MaskEngine, MutatorCache
  Resolution/     IDwPolicyProvider, AttributeProvider, StoreProvider, PolicyResolver
  Storage/        IDwPolicyStore, InMemoryPolicyStore, PolicyRule, StoreSnapshot
  Source/         PolicyQueryable, FilterSanitizer, ResultTransformer, PolicyGate
```

### 3.4 Core types

**`DwPolicyContext`** — framework-agnostic, no ASP.NET dependency in core.

```
Subjects : List<DwSubject>            (Kind, Identity) - (Role,"Admin"), (User,"123"), (Tenant,"ACME")
Values   : Dictionary<string,object?> ambient values for [DwForceWhere] - {"TenantId": 5}
Purpose  : string?                    purpose binding - "support", "reporting"
DryRun   : bool                       per-context canary
```

`DwPolicyContext.FromClaims(principal)` ships in the AspNetCore package.

> **Correction (2026-09-08, Phase 7).** It cannot be a static on `DwPolicyContext`: C# has no way to
> add a static member to a type declared in another assembly. It ships as
> `DwClaimsAdapter.CreateContextAsync(principal, options, ct)` plus an
> `HttpContext.GetPolicyContextAsync()` extension method. The context is built asynchronously because
> the narrow zone is loaded while it is built.

**`FieldPolicy`** — immutable, resolved per `(Type, FieldPath)`. The only type enforcement code ever sees.

```
CanWhere / CanSelect / CanOrder / CanGroup / CanAggregate
AllowedOperators, RequiredInWhere, ForcedPredicate
Mask, Transform, DefaultValue, Generalize, Truncate, Format
Alias, Label, Description, AllowedValues
CostWeight, Audit
IsSealed
Sources : List<PolicySource>          drives PolicyTrace and the explain endpoint
```

Static attributes and dynamic rules both compile into this shape. A third source added later — a JSON file, appsettings — is one new provider and zero enforcement changes.

**Resolution precedence**

```
1. Sealed static attribute        absolute, nothing overrides
2. Dynamic: User        |  within each level:
3. Dynamic: Role        |    exact field beats wildcard
4. Dynamic: Tenant      |    then Priority descending
5. Dynamic: Global      |    then DENY > MASK > ALLOW
6. Overridable static attribute   default only
```

Multi-role conflict at the same level resolves to DENY.

### 3.5 Snapshot capture and lazy resolution

`ApplyPolicy` captures the snapshot **reference** and the context. It resolves nothing. Field policies resolve lazily against that fixed snapshot, memoized for the life of the query.

This preserves atomicity — the snapshot reference cannot change mid-query, so a `Filter` touching `Salary` in its condition group, its orders, and its selects gets one coherent decision — while avoiding resolution of 60 properties when the filter touches 4.

### 3.6 Async boundary

Store loads are asynchronous; `ToList(filter)` is synchronous. `DwPolicyContext` is therefore built once per request through an async factory that preloads the narrow zone.

```csharp
var ctx = await policy.CreateContextAsync(User);   // once per request, async
query.ApplyPolicy(ctx).ToList(filter);               // sync, no I/O
```

This avoids adding `ApplyPolicyAsync` overloads to all 17 methods.

### 3.7 Masking mechanics

A compiled `Action<T>` mutator per `(Type, policyFingerprint)`, cached. The fingerprint hashes the resolved transform set, because two roles produce different mutators for the same type.

Reflection per row is too slow; expression trees are compiled once and reused, following the existing `CacheReflection` patterns. The walker handles nested reference paths, collection paths at any depth, and carries a cycle guard for bidirectional navigations.

---

## 4. Attribute catalogue

### 4.1 Naming

The original sketch used `Ignore*`. The agreed semantics are that a blocked WHERE **throws** rather than being ignored, so the names use `Deny` / `No` prefixes. A name that describes behaviour the library does not have is a defect in its own right.

### 4.2 Shared base

```csharp
public abstract class DwPolicyAttribute : Attribute
{
    public bool Overridable { get; set; } = false;   // sealed by default
}
```

`AttributeTargets.Property | AttributeTargets.Field`. Works on entities and DTOs alike.

### 4.3 Access control

```csharp
[Flags] public enum PolicyFeature
{ None = 0, Where = 1, Select = 2, Order = 4, Group = 8, Aggregate = 16, Segment = 32, All = 63 }

[DwDeny(PolicyFeature.Select | PolicyFeature.Order)]   // composable primitive
```

Named sugar over the primitive:

| Attribute | Equivalent to |
|---|---|
| `[DwDenied]` | `DwDeny(All)` |
| `[DwNoWhere]` | `DwDeny(Where)` |
| `[DwNoSelect]` | `DwDeny(Select)` |
| `[DwNoOrder]` | `DwDeny(Order)` |
| `[DwNoGroup]` | `DwDeny(Group)` |
| `[DwNoAggregate]` | `DwDeny(Aggregate)` |

Six one-line subclasses. Readable and composable.

```csharp
[DwOperators(Allow = new[]{ Operator.Equal, Operator.In })]
[DwOperators(Deny  = new[]{ Operator.Contains, Operator.StartsWith })]

[DwRequireWhere]
[DwRequireWhere(Operators = new[]{ Operator.Equal })]

[DwForceWhere(Operator.Equal, ContextValue = "TenantId")]   // from ctx.Values
[DwForceWhere(Operator.Equal, Value = "false")]             // constant, soft delete

[DwExpose(PolicyFeature.Where | PolicyFeature.Select)]      // only in DenyByDefault mode
```

**`[DwForceWhere]` injection shape is a correctness requirement, not a detail.** The forced predicate must wrap the caller's condition group, never merge into it. Given a caller group of `(Status = A OR Status = B)`, appending the tenant term inside that group produces `(Status = A OR Status = B OR TenantId = 5)` — an OR that returns every tenant's rows matching A or B. The correct shape is always `(entire caller group) AND TenantId = 5`, built as a new root node.

A missing `ContextValue` throws in both tiers. A tenant scope that silently fails to apply is worse than a failed request.

Class-level:

```csharp
[DwEntity(RequireGuard = true)]
public class Employee { }
```

An unguarded `ToList(filter)` on this entity throws. Without it, every policy is bypassed by simply not calling `ApplyPolicy` — this is what makes the opt-in handle a boundary rather than a suggestion.

### 4.4 Transformation

```csharp
public enum MaskStrategy { Full, Partial, Email, Phone, Regex, Fixed, Hash, Null, Tokenize }

[DwMask(MaskStrategy.Partial, KeepStart = 0, KeepEnd = 4)]        // ****1234
[DwMask(MaskStrategy.Email)]                                      // j***@d***.com
[DwMask(MaskStrategy.Regex, Pattern = @"\d", Replacement = "#")]
[DwMask(MaskStrategy.Fixed, Text = "[REDACTED]")]
[DwMask(MaskStrategy.Hash)]                                       // salt from options
[DwMask(MaskStrategy.Tokenize)]                                   // vault from options
[DwMask(MaskStrategy.Full, MaskChar = '*', PreserveLength = false)]
```

The hash salt lives in `DwPolicyOptions`, never in the attribute. A salt committed to source control is not a salt.

> **Correction (2026-09-10).** **All nine strategies ship in v3.0**, `Tokenize` included; §10's
> deferral is withdrawn. Two changes came with it.
>
> `Hash` is **HMAC-SHA256 keyed by the salt**, not `SHA256(salt || value)`. The concatenation is
> length-extendable and collides whenever a pair can be re-split — salt `secret1` with value `23`
> and salt `secret` with value `123` are the same bytes. A salt is also now refused below
> `DwPolicyOptions.MinimumHashSaltLength`, which is 16; blank still means "none configured" and is
> still refused at query time with `MissingHashSalt`.
>
> `Tokenize` replaces the value with a **random token from `DwPolicyOptions.TokenVault`**, keyed by
> a scope that defaults to the field's own path. Three vaults ship: `InMemoryTokenVault` in the core
> package, `RedisTokenVault` and `EfTokenVault` in the providers, all held to one conformance suite.
> A missing vault is refused with `MissingTokenVault` and reported by the startup scan.
>
> **What each strategy is for, now that both exist.** A hash is *derived* from its input, so whoever
> holds the salt can recompute every digest the deployment has ever emitted, and a weak salt is
> recoverable offline. A token is *drawn* and written down, so it can only be reversed by reading
> the vault — a store that can be locked, moved and revoked separately from the data. Neither closes
> equality: the same value maps to the same output under both, on purpose, because a column nobody
> can group by is a column nobody can use. §7.6 states what that leaves open.

```csharp
[DwMutate(typeof(SalaryBandTransformer))]

public interface IValueTransformer
{
    object? Transform(object? value, DwTransformContext ctx);
}
```

`DwTransformContext` carries the entity instance, the field path, and the `DwPolicyContext`, so transformers can be role-aware. The type is resolved from DI when registered, otherwise through `Activator.CreateInstance`.

```csharp
[DwDefault]              // type default - 0, null, ""
[DwDefault("N/A")]       // constant
```

C# forbids `decimal` and `DateTime` as attribute arguments, so `[DwDefault(0.00m)]` will not compile. Constants are therefore string-form and coerced at resolution by the existing `Normalizer`, which already performs string-to-typed conversion with `InvariantCulture`. Direct reuse, no new conversion code.

```csharp
[DwGeneralize(GeneralizeMode.Round,    Step = 10000)]          // salary to nearest 10k
[DwGeneralize(GeneralizeMode.Bucket,   Step = 10)]             // age to "25-34"
[DwGeneralize(GeneralizeMode.DatePart, Part = DatePart.Year)]  // birthdate to 1987
[DwGeneralize(GeneralizeMode.Truncate, Decimals = 2)]          // GPS

[DwTruncate(200, Ellipsis = "...")]
[DwFormat("yyyy-MM-dd")]
```

`Generalize` is the numeric and temporal equivalent of masking. A `decimal` cannot be star-masked, so without it the type space is only half covered.

### 4.5 Discovery

```csharp
[DwAlias("customer_name")]
[DwDescribe(Label = "Salary", Description = "Monthly gross",
            Group = "Compensation", Order = 10)]
[DwAllowedValues("Active", "Suspended", "Closed")]
```

These feed a `/schema` endpoint from which a front end can build its filter UI automatically. Aliases additionally decouple the public contract from entity refactoring and avoid leaking internal schema names.

### 4.6 Cost

```csharp
[DwCost(10)]

options.Caps.MaxPageSize        = 200;
options.Caps.MaxConditions      = 50;
options.Caps.MaxOrderFields     = 5;
options.Caps.MaxNavigationDepth = 4;
options.Caps.MaxQueryCost       = 100;
```

The library currently has no caps of any kind. A path such as `A.B.C.D.E.F` generates unbounded joins today, so this closes an existing denial-of-service surface independently of the policy feature.

### 4.7 Audit

```csharp
[DwAudit]
[DwAudit(PolicyFeature.Select)]

public interface IDwAuditSink { ValueTask Write(DwAuditEvent e); }
```

### 4.8 Startup validation

`ValidatePolicyModel()` scans every attributed type at process start and fails fast on:

- `MaskStrategy.Email` applied to a `decimal`, and every other type-incompatible mask
- Duplicate `[DwAlias]` within a type
- A `[DwMutate]` type that does not implement `IValueTransformer`
- `[DwForceWhere(ContextValue = ...)]` naming a key nothing supplies
- `[DwExpose]` present while the mode is `AllowByDefault` (dead attribute)

It emits a **warning**, not an error, when a field is masked but still orderable — see section 6.

Attribute misuse surfaces at process start rather than on a query at 3am.

---

## 5. Dynamic store

### 5.1 Rule shape

```
PolicyRule
  Id           Guid
  SubjectKind  Global | Tenant | Role | User | Custom
  SubjectKey   string?    (null for Global)
  EntityType   string
  FieldPath    string     "Salary" | "ContactInfo.Email" | "*"
  Feature      PolicyFeature (flags)
  Effect       PolicyEffect   Allow | Mask | Deny | Mutate | Default | Generalize
  Payload      string?    JSON - mask spec, default value, bucket step
  Priority     int
  Enabled      bool
  ValidFrom / ValidTo   DateTimeOffset?
  Purpose      string?
  CreatedBy / CreatedAt / UpdatedBy / UpdatedAt
```

Four additions beyond the original sketch, each earning its place:

**`Effect` separated from `Feature`.** The original model was binary enable/disable. "Manager sees Salary masked" is a third state and "Admin sees it unmasked" a fourth. `Feature` says where a rule applies; `Effect` says what happens. Without the split, masking cannot be configured at runtime at all — which was the headline use case. `PolicyEffect` is declared ascending by strictness — `Allow = 0, Mask = 1, Deny = 2` — and that ordering is load-bearing: a tie on every other precedence key resolves to the maximum, so any renumbering would quietly invert which of two conflicting rules wins.

**`FieldPath = "*"` wildcard.** Denying every field of `Employee` to role `Guest` becomes one row rather than forty. Exact field beats wildcard within the same subject level.

**`ValidFrom` / `ValidTo`.** Auto-expiring grants: a contractor sees `Salary` until a fixed date. Expiry that depends on a human remembering is expiry that does not happen.

**Audit columns on the rule itself.** Who granted this access and when. Every compliance review asks; retrofitting it later means the history is already lost.

> **Correction (2026-09-08, Phases 5–7).** The sketch above is short by four carriers, and three of
> them fail open when dropped. The shipped `PolicyRule` also holds `Forced` (a missing tenant scope
> stops being injected — cross-tenant disclosure), `RequiredOperators` (the demand to filter
> disappears and the field is readable unscoped), `AllowedOperators` (`null` means "says nothing", so
> the restriction becomes no restriction) and `Alias` (cosmetic, and the only harmless one of the
> four). Three further differences: the single `Payload` column became the typed `Transform`
> (`TransformStage?`) parsed by `PolicyPayload`; `Level` is carried explicitly; and Phase 7 added
> `Facts` (`FieldFacts?`) for `[DwDescribe]`, `[DwCost]` and `[DwAudit]`. `Feature` is spelled
> `Features`. Finally, `Effect` is **`Allow | Mask | Deny` only** — `Mutate`, `Default` and
> `Generalize` are transform *stages*, not effects, because a rule that carries a transform must not
> also decide an allowance. See the roadmap's "Design §5.1 is short by four fields".

### 5.2 Store contract

```csharp
public interface IDwPolicyStore
{
    ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct);
    ValueTask<long>          GetVersionAsync(CancellationToken ct);
    IAsyncEnumerable<long>?  WatchAsync(CancellationToken ct);   // null if unsupported
}

public interface IDwPolicyWritableStore : IDwPolicyStore
{
    ValueTask<PolicyRule> UpsertAsync(PolicyRule rule, CancellationToken ct);
    ValueTask             DeleteAsync(Guid id, CancellationToken ct);
}
```

Read-only replica deployments register only the read interface, making writes impossible by construction rather than by convention.

> **Correction (2026-09-08, Phase 5).** `IDwPolicyStore` also declares
> `ValueTask<NarrowZone> LoadNarrowAsync(IReadOnlyList<string> userIdentities, CancellationToken ct)`.
> The snapshot splits into a cached broad zone and a per-request narrow zone (§5.3), and without a
> separate narrow load a store would have to return every user's rules to serve one. A third
> interface, `IDwPolicyRefresher`, declares `ValueTask<long> RefreshAsync(CancellationToken ct)`.

### 5.3 Snapshot and zones

The whole rule set is held as one immutable object and swapped atomically. A query that begins on version 41 finishes on version 41; there are no torn reads mid-query.

Loading every rule breaks down if user-level rules exist for a million users, so the snapshot has two zones:

| Zone | Contents | Loading |
|---|---|---|
| Broad | Global + Tenant + Role | Fully loaded into the snapshot; bounded by role count |
| Narrow | User-level | On demand per subject, short TTL cache |

Per-field lookups against Redis inside a query loop would be unshippable latency. The broad zone always resolves from memory; the narrow zone is fetched once per user, not once per field.

### 5.4 Invalidation

| Store | Mechanism | Latency |
|---|---|---|
| InMemory | direct | immediate |
| Redis | pub/sub on `dw:policy:version` | milliseconds |
| EF / DB | poll `GetVersionAsync`, default 30s | up to 30s |
| Any | `IDwPolicyRefresher.RefreshAsync()` | manual or webhook |

The poll is a select against a single-row version table, cheap enough to run indefinitely.

### 5.5 Failure modes

```csharp
options.StoreFailure   = LastKnownGood;   // default; or FailClosed, or StaticOnly
options.MaxSnapshotAge = TimeSpan.FromMinutes(15);
```

Two failures are treated deliberately differently:

- **Startup load failure throws, always.** Booting into an unknown policy state means the process cannot know whether it is enforcing anything. Refusing to start is the only safe response.
- **Refresh failure keeps the last known good snapshot,** logs, and flags the instance unhealthy — but bounded by `MaxSnapshotAge`. Once the last successful load is older than the ceiling, the mode escalates to FailClosed. Without that ceiling, "Redis died six hours ago" silently becomes "we have been honouring revoked grants all afternoon."

### 5.6 EF store schema

```
DwPolicyRules      indexes: (SubjectKind, SubjectKey, EntityType), (EntityType, FieldPath)
DwPolicyVersion    single row: Version bigint, UpdatedAt - bumped on every write
```

Pure EF Core with no raw SQL, so one package serves SQL Server, Postgres, and any other EF-supported provider.

### 5.7 Admin surface

```
GET  /dw-policies/schema/{entity}     fields, types, which are configurable
GET  /dw-policies/rules?subject=...
POST /dw-policies/rules               upsert
DEL  /dw-policies/rules/{id}
POST /dw-policies/explain             effective policy per field, with reasons
POST /dw-policies/simulate            filter in, sanitized filter and trace out, no execution
GET  /dw-policies/health              version, age, last error
```

Explain output:

```
Salary
  CanSelect : true (masked)
  Mask      : Partial(keepEnd 4)
  Decided by: Rule a3f2 - Role=Manager, Priority 10
  Overrode  : [DwMask(Full)] attribute (Overridable = true)
  Ignored   : Rule b21c - Global, lower precedence
```

Sealed fields never appear: `/schema` omits them and `POST /rules` rejects them. An operator cannot even attempt to grant `NationalId`, enforced both at configuration time and at resolution time.

> **Correction (2026-09-08, Phase 7).** The endpoint list above is right as shipped. The sealed-field
> sentence needs one qualification: "sealed" is decided per *feature*, not per field. A field whose
> `[DwMask]` is sealed but whose `[DwDeny(Where)]` is `Overridable = true` is absent from `/schema`
> for the masked feature and still writable for the overridable one, so a rule targeting it is
> accepted rather than rejected. `SealedFields` is consulted per `(field, feature)` pair. A field
> sealed on every feature behaves exactly as the sentence describes.

Dry-run is per-context as well as global, so a single canary role can run in dry-run while everyone else is enforced. Global-only dry-run would force an all-or-nothing rollout.

---

## 6. Enforcement

### 6.1 PolicyQueryable surface

Mirrors all 17 existing methods: `Select`, `Where`, `Order`, `Page`, `Group`, `Filter`, `Summary`, `Segment`, `ToList`, `ToListAsync`, `ToListDynamic`, `ToListAsyncDynamic`, and the `IEnumerable` overloads. Each sanitizes, delegates to the existing extension, then transforms.

### 6.2 FilterSanitizer order of operations

```
1.  Resolve aliases          "customer_name" -> "Name"
2.  Validate caps            MaxConditions, MaxOrderFields, MaxPageSize, MaxNavigationDepth
3.  Gate WHERE               denied -> throw; bad operator -> throw
4.  Check RequireWhere       missing -> throw
5.  Compute cost             over MaxQueryCost -> throw
6.  Gate GROUP / AGGREGATE   denied -> throw
7.  Gate ORDER               denied -> drop (convenience) / throw (strict)
8.  Gate SELECT              denied -> drop; all dropped -> throw
9.  Inject ForceWhere        wrap root: (caller group) AND forced
10. Emit trace
```

Aliases resolve first because every later step needs real paths. Injection happens last because forced predicates must never be gated against the caller's own policy.

The output is a cloned `Filter`; the caller's object is untouched. The function is pure, with no database and no EF involvement, so it is directly unit-testable.

### 6.3 Error contract

```csharp
public class PolicyException : LogicException
{
    string        FieldPath;
    PolicyFeature Feature;
    PolicySource  Source;      // Attribute | Rule
    string?       RuleId;
    DwTier        Tier;
}
```

New codes follow the existing `ErrorCode` constant pattern: `FieldDeniedForWhere`, `OperatorNotAllowed`, `RequiredFilterMissing`, `AllSelectsDenied`, `QueryCostExceeded`, `MissingContextValue`, `GuardRequired`, `GroupTooSmall`.

Existing `LogicException` catch blocks continue to work, since `PolicyException` is a subclass.

### 6.4 PolicyTrace

```
PolicyTrace
  SnapshotVersion, Tier, DryRun
  Decisions : List<PolicyDecision>

PolicyDecision
  FieldPath, Feature
  Action : Allowed | Denied | Dropped | Masked | Mutated | Defaulted | Generalized | Injected
  Source, RuleId, Reason
```

Attached to `FilterResult<T>` and `SummaryResult`, and null when unguarded, so existing consumers see no shape change.

### 6.5 Performance budget

| Operation | Target |
|---|---|
| `ApplyPolicy(ctx)` | allocation only, no resolution |
| Field resolve, broad zone cached | under 1 microsecond |
| Sanitize a typical 5-condition filter | under 50 microseconds |
| Compiled mutator, cache hit | under 100 nanoseconds per row |
| Mutator compile, cache miss | one-time per (Type, fingerprint) |
| Snapshot refresh | background, never on the query path |

Guarded query overhead target is under 5 percent versus unguarded, dominated by the graph walk on large result sets.

> **Correction (2026-09-08, Phase 9).** Measured for the first time, and **the 5 percent target is
> not met**. `DynamicWhere.Benchmarks`, BenchmarkDotNet medium job, in-memory LINQ over 10,000 rows:
>
> | | Unguarded | Gating only | Gating + transforms |
> |---|---|---|---|
> | Time | 741 us | 857 us (**1.16x**) | 1,933 us (**2.61x**) |
> | Allocated | 210 KB | 409 KB (1.94x) | 3,397 KB (**16.2x**) |
>
> The per-operation targets in the table above are comfortably met: a cached field resolve is
> **239-250 ns** against a 1 us target, and sanitizing the five-condition filter is **2.8 us**
> against 50 us. What the 5 percent figure missed is that those are paid once per query while the
> transform walk is paid per row, and it must clone what it touches because values are changed after
> materialization rather than in SQL (section 2.3). Gating alone costs a flat ~15 percent;
> transformation is what turns that into 2.6x.
>
> Two caveats on the numbers. They are in-memory LINQ with no database round trip, so the policy
> layer's share is as large as it can possibly look - against a real query the I/O dominates and the
> relative overhead is much smaller. And the 5 percent was never measured before this phase; it was a
> target written at design time, not an observation. It is restated rather than defended: **budget
> roughly 15 percent for a guarded query, and roughly 2.6x for one that transforms every row of a
> large result.**

> **Correction (2026-09-10). The budget is now two budgets, and both are met.**
>
> The single 5 percent figure could never have held, and not because the code was slow. The table
> above already budgets 100 ns per row per transformed value; ten thousand rows with two transformed
> fields is 2 ms of transform work against a 700 us query. The end-to-end target and the per-row
> target contradicted each other from the day both were written. Splitting them is the fix.
>
> **Gating is budgeted at under 5 percent and measured at nothing.** Resolving every field,
> sanitizing the filter and injecting forced predicates, over a type no policy speaks to:
>
> | 10,000 rows | Time | Allocated |
> |---|---|---|
> | Unguarded | 685 us | 210 KB |
> | Guarded, nothing denied or transformed | 683 us (**1.00x**) | 220 KB (**1.05x**) |
>
> **Transformation is budgeted per value, not as a percentage.** It is paid per row per transformed
> field, so no single percentage describes it — the same guard is 1.16x over a hundred rows and
> 1.62x over ten thousand, on identical code.
>
> | Per transformed value | Budget | Measured |
> |---|---|---|
> | Time | under 100 ns | **21 ns** |
> | Allocated | one output value | **65 bytes** |
>
> **Deny-select is its own line, and belongs to the feature rather than to the guard.** Denying a
> field means the query projects instead of returning entities, which costs 1.15x in time and 1.95x
> in allocations at ten thousand rows whatever the policy layer does afterwards.
>
> **What changed in the code.** The walker was rewritten around the fact that it is the only part of
> this layer whose cost grows with the result. The root object set is built once for the whole result
> rather than once per transformed path; the property and its compiled accessors are resolved once per
> runtime type rather than once per row; and `DwTransformContext` became a `readonly struct`, because
> one was being allocated for every value of every row. Against the same benchmark on the same
> machine, the transforming case went from **2.61x to 1.62x** in time and from **16.2x to 7.1x** in
> allocations.
>
> The two caveats from the Phase 9 note still hold and still matter. These are in-memory LINQ numbers
> with no database round trip, so the policy layer's share is as large as it can possibly look. And
> the per-operation targets were always met: a cached field resolve is 232-234 ns against a 1 us
> target, and sanitizing the five-condition filter is 2.8 us against 50 us.

---

## 7. Security analysis

Five disclosure channels exist that no per-field attribute closes on its own. Three are specific to this library because of features it has that comparable libraries do not.

> **Correction (2026-09-08, Phase 8).** The five channels below are closed by **seven** mitigations,
> and `PolicyInferenceTests` attacks all seven. Two of them are not channels in the list: an
> unguarded call on `[DwEntity(RequirePolicy = true)]` must throw, and an empty policy store must
> leave attributes enforcing rather than resolving to Allow. Their controls shipped in Phases 1 and 4
> and were not attacked until Phase 8 attacked them. See design §8.5, whose table already had seven
> rows.

### 7.1 Set operations reconstruct denied fields

`Segment` composes Union, Intersect, and Except across subqueries. Where a field is deny-select but allow-where, `AllEmployees EXCEPT (AllEmployees WHERE Salary > 100000)` returns exactly the set of people earning under 100k — by name, with the salary column never selected. The protected value is reconstructed from set membership.

Mitigation: policy applies to every segment independently, and in the strict tier a field that is deny-select is automatically deny-where inside a `Segment`.

### 7.2 Aggregates over singleton groups

`SUM`, `MAX`, and `MIN` execute in SQL against real values, before any mask can apply. `GROUP BY Department` with `MAX(Salary)` over a department of one returns that person's exact salary.

Mitigation: aggregating a masked field is denied by default and opted into with `[DwMask(AllowAggregate = true)]`. A k-anonymity guard, `options.MinGroupSize`, suppresses groups smaller than k. Without the group-size floor, `AllowAggregate` is a hole rather than a feature.

> **Correction (2026-09-08, Phase 8).** The mitigation covers **every transform, not masks alone**.
> `[DwMutate]`, `[DwDefault]`, `[DwGeneralize]`, `[DwTruncate]` and `[DwFormat]` all leave the real
> value readable through `SUM`, `MAX` or `MIN`, so all six attributes carry `AllowAggregate` and
> `MinGroupSize`, and the denial lives in `PolicyResolver` where the transform chain is elected —
> one place that covers attributes and runtime rules alike. `DwCaps.MinGroupSize` defaults to **1**,
> i.e. off: any other default would silently change the result of an existing grouping query for
> anyone upgrading. It is a deliberate opt-in, and the one control in this document that a reader
> cannot infer from the API.

> **Correction (2026-09-10).** **The floor ships on, at 5.** The compatibility argument above does
> not survive being looked at: the floor applies only to a *guarded* summary, guarded queries are
> new in this release, and so there is no caller anywhere whose results shipping it on can change.
>
> The setting starts **unset** rather than at one, which is what makes both halves possible. Unset
> means `DwCaps.DefaultMinGroupSize`. An explicit `MinGroupSize = 1` means no floor and is honoured
> in production, with nothing refused and nothing warned about — a deployment that wants singleton
> groups is entitled to them and should not have to fight the library for them.
> `DwCaps.IsMinGroupSizeSet` reports which of the two happened, because "off" and "never configured"
> are different facts, and a control that cannot tell them apart can only trap the person who meant
> it.

### 7.3 TotalCount cardinality disclosure

`ToList` computes `query.Count()` on the pre-pagination query. Filtering `Salary > 200000` and reading `TotalCount` counts the high earners without selecting anything.

This is inherent to permitting WHERE on a protected field. The actual control is `[DwOperators]` restricting the field to `Equal` and `In`. Documented consequence, not a defect.

### 7.4 Order plus paging is a binary search

Sorting by a masked field ranks real values. Paging through a known set reveals relative magnitude, and combined with range filters it converges on exact values.

Mitigation: startup validation emits a warning when a field is masked but still orderable. `[DwNoOrder]` remains the explicit fix; the engine does not decide silently.

### 7.5 getQueryString leaks the generated SQL

Returning raw SQL exposes injected tenant predicates and the column names of denied fields. The strict tier throws; the convenience tier allows it, documented.

### 7.6 A stable mask discloses equality

> **Added 2026-09-10, with `Tokenize`.**

`Hash` and `Tokenize` both promise that one value always produces one output, which is what lets a
caller group by a masked column, join two results on it, and count distinct subjects without ever
being shown a value. That promise is itself a disclosure, and no configuration removes it.

Anyone who can write a chosen value and read the column back masked learns that value's output, and
can then recognise it in every other row. The attack needs write access to the protected data and
read access to the masked column, and what it yields is membership rather than the value itself.

There is no mitigation, because the property being exploited is the feature. What differs between
the two strategies is everything *around* it:

| | `Hash` | `Tokenize` |
|---|---|---|
| Output is | derived from the value | drawn at random |
| Reversed by | holding the salt | reading the vault |
| A weak secret | is brute-forced offline | does not exist |
| Rotating it | changes every value at once | is a store migration |
| Discloses equality | yes | yes |

So `Tokenize` closes the *derivation* class — recomputation, and offline dictionary work against a
guessable salt — and closes nothing else. A deployment that cannot accept equality disclosure has to
stop preserving equality, which means `Fixed`, `Null`, or denying the field outright.

---

## 8. Testing

Validation is by the project's own suite, across six layers.

### 8.1 Pure unit, no database

| File | Covers |
|---|---|
| `PolicyResolutionTests.cs` | six-level precedence, DENY over MASK over ALLOW, exact beats wildcard, multi-role conflict, sealed unliftable, ValidFrom/ValidTo expiry |
| `PolicySanitizerTests.cs` | ten-step order of operations, clone-not-mutate, alias resolution, cap rejection, cost budget |
| `PolicyMaskTests.cs` | nine strategies against every type category, PreserveLength, MaskChar, null handling |
| `PolicyValidationTests.cs` | every startup fail-fast rule, plus the masked-but-orderable warning |

The precedence matrix is exhaustive rather than sampled. Six levels against Allow, Deny, and Mask, present and absent, is exactly where a subtle bug hides until a customer's data leaks.

A required sanitizer test pins the `[DwForceWhere]` wrapping shape:

```
input:  ConditionGroup { Or: [Status=A, Status=B] }
assert: root is AND
assert: root.Left is the original Or group, unmodified
assert: root.Right is TenantId == 5
```

Written so it fails loudly if the injection is ever "simplified" into a merge.

### 8.2 SQLite integration

New `Domain/Secured/` fixture entities decorated with all attributes. The existing fifteen test files stay untouched, plus one new test asserting that attributes have zero effect on unguarded queries.

| File | Covers |
|---|---|
| `PolicyGuardedQueryTests.cs` | every attribute end to end, both tiers |
| `PolicyTraceTests.cs` | every Action value emitted, SnapshotVersion correct |
| `PolicyDryRunTests.cs` | dry-run data identical to unguarded, trace fully populated, per-context canary |
| `PolicyPathShapeTests.cs` | direct, nested reference, collection, deep nested collection, cycle guard |

### 8.3 SQL Server API project

New `9_PolicyTestController.cs` and `__PolicyAdminController.cs`, matching the existing `__CacheController.cs` naming.

The most important test in the entire suite:

```
1. Guarded query on Employee with Salary masked, returns "****"
2. Assert EntityState.Detached on every returned entity
3. Modify an unrelated entity in the SAME DbContext
4. SaveChanges()
5. Re-query Salary with raw SQL
6. Assert the original value is intact
```

If `AsNoTracking` ever regresses, this fails. Nothing else catches it, and the production failure mode is silent permanent data destruction. It is a blocking CI test.

Also SQL-Server-only: the injected `[DwForceWhere]` predicate appears in the real generated SQL; `getQueryString` is gated in the strict tier; masked values never enter the query plan cache; the `DwPolicyRules` and `DwPolicyVersion` migrations apply cleanly.

> **Correction (2026-09-08, Phase 9).** `DynamicWhere.API` runs on **PostgreSQL** (Npgsql 8.0.11),
> not SQL Server. Everything in this section applies unchanged — the checks are about real generated
> SQL against a real relational database, not about a particular vendor. Both controllers shipped as
> named: `9_PolicyTestController.cs` and `__PolicyAdminController.cs`. The blocking `AsNoTracking`
> test is `GET /api/PolicyTest/tracking/no-writeback`.

### 8.4 Store conformance

One shared suite, run against InMemory, Redis, and EF:

- load, version, watch and poll
- atomic swap — a query on version 41 finishes on version 41
- broad and narrow zone split
- startup load failure throws
- refresh failure falls back to last known good
- MaxSnapshotAge exceeded escalates to FailClosed
- sealed field rejected on upsert
- read-only store cannot write

Any future store provider inherits the whole suite.

### 8.5 Security regression suite

`PolicyInferenceTests.cs`. Each test reproduces the attack, then asserts the mitigation blocks it, so removing a mitigation turns the test red.

| Attack | Assertion |
|---|---|
| Except reconstructs a deny-select field | strict tier blocks where-on-deny-select inside Segment |
| Singleton group with MAX reveals an individual | AllowAggregate false by default; MinGroupSize suppresses |
| TotalCount cardinality probe | `[DwOperators]` blocks the range operator |
| Order plus page binary search on a masked field | startup warning emitted; `[DwNoOrder]` blocks |
| getQueryString leaks the tenant predicate | strict tier throws |
| Unguarded call on `[DwEntity(RequireGuard)]` | throws |
| Empty policy store | attributes still enforce |

### 8.6 Benchmarks

Guarded against unguarded, asserting the 5 percent budget. Mutator cache hit and miss, resolution cost, sanitize cost. CI fails on regression beyond threshold.

> **Correction (2026-09-10).** **CI does not fail on a benchmark regression, and deliberately does
> not.** A timing gate on a shared runner fails for a neighbour's build, and the workflow it would
> gate is the one that publishes to NuGet — a release blocked by noise is a worse outcome than a
> regression caught on the next manual run. `DynamicWhere.Benchmarks` is run by hand:
>
> ```
> dotnet run -c Release --project DynamicWhere.Benchmarks -- --filter "*PolicyBenchmarks*" --job medium
> ```
>
> Six benchmarks at two row counts. `GuardedNoPolicy` is the one the gating budget is held to;
> `GuardedGatingOnly` prices deny-select; `Guarded` prices the transform walk. Without all three the
> end-to-end number cannot say which part moved. See the correction to §6.5.

### 8.7 Coverage grid

| Axis | Cases |
|---|---|
| Features by tier | 24 x 2 = 48 |
| Precedence matrix | 6 levels x 3 effects, full grid |
| Mask strategies by type | 9 x 6, most combinations must reject |
| Path shapes | 4 |
| Store implementations by failure mode | 3 x 4 |
| Inference attacks | 7 |

### 8.8 Framework version leg

The test project runs EF Core 8 on SQLite while the library floor is 6.0.22. Expression-tree APIs used by the compiled mutator and by predicate injection differ subtly between 6 and 8, so CI needs a leg building against 6.0.22. Otherwise the floor claimed in the csproj is untested.

---

## 9. Non-goals

- Row-level security beyond `[DwForceWhere]` predicate injection. Row filtering by arbitrary expression is out of scope.
- Column-level encryption. Masking hides values in output; it is not a cryptographic control at rest.
- A UI for the admin API. The package ships endpoints and a schema contract, not screens.
- Policy authoring DSL. Rules are data rows and attributes, not a language.
- Projection-time masking. Considered and deferred; see 2.3.

---

## 10. Open items for the implementation plan

All four were settled before or during Phase 1. Recorded here as the decisions the later phases build on.

- `Segment` has its own `PolicyFeature` flag, `32`; `All` is `63`. Resolved in Phase 1.
- `MinGroupSize` is a global option with a per-field attribute override. Lands in Phase 8.
- ~~Tokenize is deferred to v3.1. The other eight mask strategies ship in v3.0.~~ **Withdrawn
  2026-09-10: all nine ship in v3.0, with a vault behind the ninth. See §4.4 and §7.6.**
- The entry method is `ApplyPolicy(ctx)`; the class-level flag is `[DwEntity(RequirePolicy = true)]`.
