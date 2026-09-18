# DynamicWhere.ex

**JSON-driven queries for Entity Framework Core.**

[![NuGet Version](https://img.shields.io/nuget/v/DynamicWhere.ex?style=flat-square&color=8b5cf6&label=NuGet)](https://www.nuget.org/packages/DynamicWhere.ex)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DynamicWhere.ex?style=flat-square&color=22c55e)](https://www.nuget.org/packages/DynamicWhere.ex)
[![License](https://img.shields.io/badge/license-MIT-8b5cf6?style=flat-square)](https://github.com/Sajadh92/DynamicWhere.ex/blob/master/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%2B-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/docs-doc.dynamicwhere.com-8b5cf6?style=flat-square)](https://doc.dynamicwhere.com)

A powerful, versatile library for dynamically composing complex **filter, sort, paginate, group, aggregate, and set-operation** (Union / Intersect / Except) expressions in Entity Framework Core applications — all driven by simple JSON objects from any front-end or API consumer.

**Full reference, JSON cookbook, and tuning guide → [doc.dynamicwhere.com](https://doc.dynamicwhere.com)**

### Using an AI coding agent?

Point it at **[doc.dynamicwhere.com/llms.txt](https://doc.dynamicwhere.com/llms.txt)** — the entire API
surface in one plain-text file: every public type and member of the four packages, the JSON on the wire,
every error string, the whole policy layer, the cache, and the traps that produce code which compiles and
is quietly wrong.

```text
Read https://doc.dynamicwhere.com/llms.txt before writing any
DynamicWhere.ex code. It is the complete API surface.
```

Works with Claude, Copilot, Cursor, Codex or anything else that can read a URL. If yours cannot,
copy it from [the reference page](https://doc.dynamicwhere.com/docs/ai).

---

## Why DynamicWhere.ex?

Stop concatenating LINQ predicates by hand. Your front-end sends one JSON shape; the back-end calls a single extension method. You get back a strongly-typed, paginated result.

- **JSON in → `IQueryable<T>` out.** No string LINQ. No manual expression trees.
- **Three composable shapes** — `Filter`, `Segment`, `Summary` — cover where, set operations, and group-by reporting.
- **Twenty-one extension methods** on `IQueryable<T>` and `IEnumerable<T>`.
- **Nested navigation** through references and collections, with auto-wrapped `.Any()` lambdas where needed.
- **Heterogeneous `Condition.Values`** — pass raw numbers, booleans, strings; normalized per `DataType`.
- **Thread-safe reflection cache** with FIFO / LRU / LFU eviction and five tuned presets.
- **Field-level policies** *(new in 3.0)* — decide per caller what may be filtered, sorted, selected, grouped, aggregated and seen. Opt-in: nothing enforces until you ask.
- **Free Forever.** Targets .NET 6, 7, 8, 9, 10.

---

## Install

```bash
dotnet add package DynamicWhere.ex --version 3.1.0
```

Or via Package Manager:

```powershell
Install-Package DynamicWhere.ex -Version 3.1.0
```

Dependencies (restored automatically):

| Package | Version |
|---------|--------:|
| `Microsoft.EntityFrameworkCore` | `6.0.22` |
| `System.Linq.Dynamic.Core` | `1.6.7` |
| `Microsoft.Extensions.Configuration.Abstractions` | `6.0.0` |
| `Microsoft.Extensions.Configuration.Binder` | `6.0.0` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `6.0.0` |

---

## Quick Start

**Front-end / API body — pure JSON:**

```json
{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "Price",         "dataType": "Number", "operator": "GreaterThan", "values": [50] },
      { "sort": 2, "field": "Category.Name", "dataType": "Text",   "operator": "IEqual",      "values": ["electronics"] }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Price", "Category.Name"],
  "orders":  [{ "sort": 1, "field": "Price", "direction": "Descending" }],
  "page":    { "pageNumber": 1, "pageSize": 10 }
}
```

**Back-end — one method call:**

```csharp
using DynamicWhere.ex.Source;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Result;

app.MapPost("/products/search", async (Filter filter, AppDbContext db) =>
{
    FilterResult<Product> result = await db.Products.ToListAsync(filter);
    return Results.Ok(result);
});
```

**Response shape (`FilterResult<Product>`):**

```json
{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    {
      "id": 7,
      "name": "Laptop Pro",
      "price": 1299.99,
      "isActive": false,
      "createdAt": "0001-01-01T00:00:00",
      "category": { "id": 5, "name": "Electronics" }
    }
  ],
  "queryString": null
}
```

A typed row is a whole `Product`: `selects` decides which members are read, and the rest hold their defaults. `ToListAsyncDynamic` returns only the selected members.

That's the whole loop. Full walk-through in **[Quick Start](https://doc.dynamicwhere.com/docs/quick-start)**.

---

## What's inside

### Three composable shapes

| Shape | Pipeline | Use when |
|-------|----------|----------|
| **[`Filter`](https://doc.dynamicwhere.com/docs/classes/filter)** | where → order → page → select | Standard list / search / detail endpoints |
| **[`Segment`](https://doc.dynamicwhere.com/docs/classes/segment)** | set1 ∪/∩/∖ set2 ∪/∩/∖ set3 → order → page | UNION / INTERSECT / EXCEPT across multiple condition sets |
| **[`Summary`](https://doc.dynamicwhere.com/docs/classes/summary)** | where → group → having → order → page | Aggregate reporting (`GROUP BY` + `SUM` / `AVG` / `COUNT` …) |

### Twenty-one extension methods

Projection, filtering, composition, and materialization on `IQueryable<T>` and `IEnumerable<T>`:

| Group | Methods |
|-------|---------|
| **Projection** | `.Select<T>(fields)` · `.SelectDynamic<T>(fields)` |
| **Filtering** | `.Where<T>(Condition)` · `.Where<T>(ConditionGroup)` |
| **Composition** | `.Order<T>` · `.Page<T>` · `.Group<T>` · `.Filter<T>` · `.FilterDynamic<T>` · `.Summary<T>` |
| **Materialization** | `.ToList<T>(Filter)` · `.ToListAsync<T>(Filter)` · `.ToListDynamic<T>(Filter)` · `.ToListAsyncDynamic<T>(Filter)` · `.ToList<T>(Summary)` · `.ToListAsync<T>(Summary)` · `.ToListAsync<T>(Segment)` |

Full signatures, validations, and return types → **[Extension Methods Reference](https://doc.dynamicwhere.com/docs/extensions)**.

### Operators & data types

Twenty-eight comparison operators across seven data types — case-sensitive **and** case-insensitive variants of every text operation:

- **Equality:** `Equal` · `IEqual` · `NotEqual` · `INotEqual`
- **Substring:** `Contains` · `IContains` · `NotContains` · `INotContains` · `StartsWith` · `IStartsWith` · `EndsWith` · `IEndsWith` (+ `Not*` and `I*` of each)
- **Set:** `In` · `IIn` · `NotIn` · `INotIn`
- **Range:** `GreaterThan` · `GreaterThanOrEqual` · `LessThan` · `LessThanOrEqual` · `Between` · `NotBetween`
- **Null:** `IsNull` · `IsNotNull`

Data types: `Text` · `Guid` · `Number` · `Boolean` · `DateTime` · `Date` · `Enum`. Full matrix → **[DataType reference](https://doc.dynamicwhere.com/docs/enums/data-type)**.

### Aggregations

`Count` · `CountDistinct` · `Sumation` · `Average` · `Minimum` · `Maximum` · `FirstOrDefault` · `LastOrDefault`. With optional `Having` post-filter referencing aggregate aliases.

### Nested navigation

Dotted paths through reference **and** collection properties, with `.Any()` lambdas inserted automatically where the path crosses a collection.

```json
{ "field": "Orders.OrderItems.ProductName", "dataType": "Text",
  "operator": "IContains", "values": ["laptop"] }
```

Becomes:

```
Orders.Any(i1 => i1.OrderItems.Any(i2 =>
    i2.ProductName != null && i2.ProductName.ToLower().Contains("laptop")))
```

Sorting takes the same paths. `.Any()` yields a boolean, so ordering reduces each collection segment to one comparable value instead — the **smallest** element ascending, the **largest** descending:

```json
{ "sort": 1, "field": "OrderItems.Product.Name", "direction": "Ascending" }
```

Becomes:

```
OrderItems.Min(Product.Name) asc
```

Rows with an empty collection sort as `null` (or the type default for non-nullable value types). A path may not *end* on a collection of entities — sort by a scalar inside it (`Tags` ✗ → `Tags.Value` ✓).

---

## Field-level policies

*New in 3.0. Entirely opt-in — a project with no policy attributes and no `DwPolicy.Configure` call behaves exactly as 2.1.5.*

The library accepts a JSON `Filter` from any caller and turns it into a query. Policies add the missing question: **who is asking, and what are they allowed to see?**

```csharp
// Once, at startup.
DwPolicy.Configure(new DwPolicyOptions { Tier = DwTier.Convenience, HashSalt = secret });

// Once per request.
var caller = await DwPolicy.PrepareAsync(
    new DwPolicyContext()
        .WithSubject(DwSubjectKind.User, userId)
        .WithSubject(DwSubjectKind.Role, "Support"));

// Then query through the guarded handle instead of the raw IQueryable.
var result = await db.Employees.ApplyPolicy(caller).ToListAsync(filter);
```

Requests are sanitized before the query is built; results are transformed after they materialize. The query engine itself is unchanged.

```csharp
[DwEntity(RequirePolicy = true)]          // an unguarded read throws instead of returning rows
public class Employee
{
    [DwMask(MaskStrategy.Email), DwNoOrder]
    public string Email { get; set; }      // s*************@c******.com on the way out

    [DwForceWhere(Operator.Equal, Value = "true")]
    public bool IsActive { get; set; }     // ANDed into every guarded query, asked for or not

    [DwGeneralize(GeneralizeMode.Round, Step = 5000, AllowAggregate = true, MinGroupSize = 5)]
    [DwNoOrder, DwAudit, DwCost(10)]
    public decimal Salary { get; set; }    // rounded, aggregatable only over groups of 5+

    [DwDenied]
    public JsonDocument? WorkSchedule { get; set; }   // absent from /schema, rejected by POST /rules
}
```

**Six features, per field:** `Where` · `Select` · `Order` · `Group` · `Aggregate` · `Segment`.

| Attribute | What it does |
|---|---|
| `[DwDeny]`, `[DwDenied]`, `[DwNoWhere]`, `[DwNoSelect]`, `[DwNoOrder]`, `[DwNoGroup]`, `[DwNoAggregate]` | Refuse features for a field |
| `[DwOperators]` | Restrict which operators may target it |
| `[DwAlias]` | Give it a public name, renamed back on the way out |
| `[DwForceWhere]` | Add a predicate to every guarded query — tenant scope, soft delete, ownership |
| `[DwRequireWhere]` | Make a filter on it mandatory |
| `[DwMask]` | Obscure the value — 9 strategies: `Full` `Partial` `Email` `Phone` `Regex` `Fixed` `Hash` `Null` `Tokenize` |
| `[DwMutate]`, `[DwDefault]`, `[DwGeneralize]`, `[DwTruncate]`, `[DwFormat]` | The other five transforms |
| `[DwDescribe]`, `[DwAllowedValues]`, `[DwCost]`, `[DwAudit]` | Schema discovery, query budget, audit trail |

### Sealed by default

Attributes cannot be lifted by a runtime rule unless you mark them `Overridable = true`. Six precedence levels decide every field, sealed attributes first and overridable attributes last, with dynamic user, role, tenant and global rules in between.

### Configuration, and a field picker that fits on a screen

The whole posture binds from `appsettings.json`, environment variables or a vault. A key nothing answers to **refuses to start**, because a misspelt `MinGropSize` sitting in a file doing nothing is exactly the failure the rest of this layer exists to prevent.

```csharp
builder.Services.AddDwPolicies(
    builder.Configuration.GetSection("DynamicWhere:Policies"),
    options => options.Entities.Expose<Employee>("Employee"));
```

`POST /dw-policies/schema` describes an entity for a filter UI, two levels deep by default and drillable a subtree at a time. The response is flat with a parent on every field and node, so a tree is one grouping pass on the client. → **[Admin API](https://doc.dynamicwhere.com/docs/policies/admin)**

### Rules without a redeploy

An optional store supplies rules at runtime, split into a cached broad zone and a per-request narrow zone. In-memory ships in the core package; **Redis** and **Entity Framework Core** are separate packages. A store can never grant a field the source code seals.

### The control you would not guess: `MinGroupSize`

`SUM`, `MAX` and `MIN` run **in SQL, against the stored value**, before any mask can apply — so `MAX(Salary)` over a department of one returns that person's exact pay. Aggregating a transformed field is therefore **denied by default**, opted into with `AllowAggregate = true`, and bounded by `MinGroupSize`, which suppresses any group smaller than *k*.

It **defaults to 5**. Write `MinGroupSize = 1` to switch it off and it is off, in production, with nothing refused and nothing warned about — the setting starts unset rather than at one precisely so that "off" and "never configured" stay different sentences. → **[Security & k-anonymity](https://doc.dynamicwhere.com/docs/policies/security)**

### Hiding a value you still want to group by

`Hash` and `Tokenize` both keep a column groupable and joinable while hiding what is in it. The difference is where the secret lives.

A hash is **computed from the value**, with HMAC-SHA256 keyed by `HashSalt` — at least 16 characters, or it is refused where it is written. Whoever holds that salt can recompute every digest the deployment ever emitted.

A token is **drawn at random** and written into `TokenVault`, so the only way back is to read the vault: a store you can lock, move and revoke separately from the data. Three ship — in-memory in the core package, Redis and Entity Framework Core in the providers — all held to one conformance suite.

```csharp
new DwPolicyOptions { HashSalt = secret, TokenVault = new RedisTokenVault(redis) }
```

Neither closes equality, and that is the point of both: the same value maps to the same output so the column stays usable, which also means anyone who can write a chosen value and read it back learns that one value's stand-in. → **[Transforms](https://doc.dynamicwhere.com/docs/policies/transforms)**

### The four packages

| Package | What it adds |
|---|---|
| `DynamicWhere.ex` | Everything above |
| `DynamicWhere.ex.Policies.Redis` | Rules in Redis, pub/sub invalidation with a poll behind it |
| `DynamicWhere.ex.Policies.EntityFrameworkCore` | Rules in any EF Core provider |
| `DynamicWhere.ex.Policies.AspNetCore` | Admin API — schema, rules, explain, simulate, health. Refuses to mount without a named authorization policy |

**Full guide → [doc.dynamicwhere.com/docs/policies](https://doc.dynamicwhere.com/docs/policies)**

---

## Reflection cache

A thread-safe `ConcurrentDictionary`-backed cache across three stores (TypeProperties · PropertyPath · CollectionElementType) eliminates reflection overhead on repeated queries. Three eviction strategies, and five preset factories beside the default:

| Preset | MaxSize | Eviction | Use case |
|--------|--------:|:--------:|----------|
| `new CacheOptions()` | 1000 | LRU | General purpose |
| `ForHighMemoryEnvironment()` | 5000 | LRU | Servers with ample RAM |
| `ForLowMemoryEnvironment()`  | 250  | LFU | Constrained environments |
| `ForDevelopment()` | 100 | FIFO | Testing & debugging |
| `ForHighFrequencyAccess()` | 2000 | LFU | Repeated queries on same types |
| `ForTemporalAccess()` | 1500 | LRU | Recent-access-heavy workloads |

```csharp
using DynamicWhere.ex.Optimization.Cache.Source;

CacheExpose.Configure(CacheOptions.ForHighMemoryEnvironment());
CacheExpose.WarmupCache<Product>("Name", "Category.Name", "Price");
```

Full tuning guide → **[Cache & Optimization](https://doc.dynamicwhere.com/docs/cache)**.

---

## Error handling

Every validation failure throws **`LogicException`** with a structured error code. Catch at your API boundary and surface as a 400:

```csharp
try
{
    var result = await db.Products.ToListAsync(filter);
    return Results.Ok(result);
}
catch (LogicException ex)
{
    return Results.BadRequest(new { code = ex.Message });
}
```

Full code reference → **[Error Codes](https://doc.dynamicwhere.com/docs/errors)**.

---

## Documentation

The complete reference — every enum, class, extension method, validation rule, JSON example, and cache option — lives on the official site:

### **[→ doc.dynamicwhere.com](https://doc.dynamicwhere.com)**

| Section | What's there |
|---------|--------------|
| [Getting Started](https://doc.dynamicwhere.com/docs)              | Introduction, installation, quick start |
| [Enums](https://doc.dynamicwhere.com/docs/enums)                  | Every DataType, Operator, Connector, Direction, Intersection, Aggregator, Cache enum |
| [Classes](https://doc.dynamicwhere.com/docs/classes)              | Condition, ConditionGroup, ConditionSet, OrderBy, GroupBy, AggregateBy, PageBy, Filter, Segment, Summary, Result types |
| [Extension Methods](https://doc.dynamicwhere.com/docs/extensions) | All 17 methods with signatures, validations, examples |
| [Validation Rules](https://doc.dynamicwhere.com/docs/validation)  | What's checked and what throws |
| [JSON Cookbook](https://doc.dynamicwhere.com/docs/examples)       | 13 copy-pasteable end-to-end examples |
| [Field-Level Policies](https://doc.dynamicwhere.com/docs/policies) | Attributes, precedence, masking, dynamic rules, admin API, k-anonymity |
| [Cache & Optimization](https://doc.dynamicwhere.com/docs/cache)   | Architecture, stores, options, presets, monitoring |
| [Error Codes](https://doc.dynamicwhere.com/docs/errors)           | Every `LogicException` message |
| [Breaking Changes](https://doc.dynamicwhere.com/docs/breaking-changes) | Known limits and migration notes |

---

## Version 3.1.0 highlights

**Upgrade note — eleven behaviour changes, listed first. Read these before bumping.**

- **Fixed (security): members named `Root`, `It` or `Parent`.** The expression parser read them as its `root` / `it` / `parent` keywords, so `Root.Name` addressed the row's own `Name`, and `Parent` threw. Under `ApplyPolicy` a projection of `Root.Name` returned a `[DwDenied]` column, and a `[DwForceWhere]` scope reached through such a navigation filtered the wrong column. Expressions are now parsed with the keywords off, through a configuration of the library's own: `ParsingConfig.Default` is no longer read. The words the parser does keep — `new`, `iif`, `np`, `isnull`, `is`, `as`, `cast`, `true`, `false`, `null` — are refused by name when one begins a field path, in every clause and guarded or not, with `LogicException` `FieldPath[{path}]StartsWithReservedName`. Nine of them used to throw, and a member named `Null` was read as the null literal, so the query returned no rows and no error. Only a path's first segment is affected: `Owner.New` names the member, and the remedy for such a column is to rename the property and map it with `[Column("New")]`.
- **Fixed: `DateTimeOffset` columns.** Every comparison on a `DateTimeOffset` member threw, and `DataType.Date` on any nullable date member threw with it. The predicate is now built from the member's own type — a null guard only where the member can be null, a literal of the member's type, `.Value.Date` under the guard — and `IsNull` / `IsNotNull` on a non-nullable date member of the entity itself answer `false` / `true`. Reached through a navigation, they test the navigation. Verified against Npgsql `timestamptz`.
- **Changed: a date value is ISO 8601 or a declared format, never a guess.** The server's culture used to decide, so `01/09/2026` was 1 September on one server and 9 January on another. Now ISO 8601 extended calendar dates (`2026-09-01`, with or without a time and zone) and year-first dates are accepted everywhere; a day/month-first date is refused with the new `AmbiguousDateFormat` unless the deployment declares its order once — `DwDates.Configure(o => o.Formats.Add("dd/MM/yyyy"))`. `DateTimeOffset` values are normalised to UTC, and `DateOnly` columns can be filtered at all. `Configure` refuses a format whose own text ISO 8601 or a year-first date already reads, such as `yyyy-MM-dd'T'HH:mm:ss'Z'`: declaring one could only change what such a value means.
- **Changed: an unprepared context is refused with or without a store.** `ApplyPolicy(ctx)` throws `PolicyContextNotPrepared` for a context that never went through `DwPolicy.PrepareAsync`, with or without a store configured. An attributes-only deployment used to accept it and would have started refusing the day it gained a store.
- **Changed: `Segment` set operations run in the database.** `Intersect` returned nothing, `Except` removed nothing and `Union` counted a row once per set whenever the query was untracked, projected with `Selects`, or guarded by `ApplyPolicy` — the sets were combined in memory by object reference. They are now one query: `Union` and `Intersect` combine the sets' conditions and `Except` matches rows by primary key, then the rows are ordered, paged and counted in SQL like a filter, so only the page is read. Sorting follows the database collation, and `Orders` apply before `Selects`.
- **Changed: `PageCount` on an unpaged result is `1`** on filter, summary and segment results alike — it was `TotalCount` for the first two and `0` for a segment with condition sets.
- **Changed: two new caps refuse guarded requests 3.0.0 ran.** `DwCaps.MaxConditionDepth` (default 10) bounds how deeply condition groups nest, and `DwCaps.MaxConditionSets` (default 10) how many condition sets a `Segment` may carry, empty sets included. A guarded request nested eleven levels deep, or a segment with eleven sets, is now refused with `CapExceeded` unless the deployment raises the cap. Unguarded calls are not affected.
- **Changed: two more caps, and a `Count` that costs.** `DwCaps.MaxConditionValues` (default 1000) bounds the values one condition carries — an `In` was one comparison per value for the price of one condition — and `DwCaps.MaxAggregates` (default 50) the aggregates one summary computes. A guarded request over either is refused with `CapExceeded`. An aggregate with no field, such as a `Count`, is now charged `DefaultFieldCost` toward `MaxQueryCost`; it was free. Every count cap is checked before any field name is resolved, so an oversized request is refused with `CapExceeded` even when it also names a field that does not exist. Unguarded calls are not affected.
- **Changed: a stable code where a sentence was.** `Select` on a type it cannot construct throws `SelectTypeMustHaveParameterlessConstructor`, with the type name on the new `LogicException.Subject`.
- **Changed: the strict tier keeps the policy trace off results.** Under `DwTier.Strict`, `FilterResult<T>.Policy`, `SummaryResult.Policy` and `SegmentResult<T>.Policy` are null unless `DwPolicyOptions.IncludeTraceInResult = true`. The trace names every dropped field, the attribute or rule that sealed it and every injected predicate, and an API that serializes its result hands all of that to the caller. `PolicyQueryable<T>.LastTrace` still holds it, and the convenience tier still returns it unless the option is `false`.
- **Changed: under the strict tier an unknown field and a denied field answer alike.** A name that matches nothing is refused like a `[DwDenied]` field, with that clause's `FieldDeniedFor…` code, instead of `LogicException` `ConditionMustHasValidFieldName`. Every such refusal carries `FieldPath` `"*"` and no `RuleId` or `SourceOrigin`, and a cap refusal names no path, so a caller can no longer list the columns they may not see one guess at a time. The side doors are shut too: inside a segment every field refusal is `FieldDeniedForSegment`, `MaxQueryCost` is checked after the field gates so a `[DwCost]` weight cannot tell a hidden field from a missing one, and `MissingContextValue` names neither the scope's column nor its context key. The trace keeps the real path; the convenience tier and dry runs are unchanged.
- **Fixed (security): a long `In` list ended the process.** `In` and `NotIn` (and `IIn` / `INotIn` on text) joined their values into one flat `||` / `&&` chain, one level of expression nesting per value, and EF Core walks that tree recursively: a single condition carrying about seven hundred values overflowed the request thread's stack, guarded or not, and a stack overflow cannot be caught. A list longer than 32 values is now a balanced tree of short chains; a list of 32 or fewer is written exactly as before, and the rows returned are the same.
- **Fixed: a local `DateTime` names its own moment on a `DateTimeOffset` member.** A C# `DateTime` whose `Kind` is `Local` — `DateTime.Now`, or one Newtonsoft.Json read from text with an offset — placed in `Values` under `DataType.DateTime` is written with its offset (`2026-09-17T15:00:00+03:00`). A `DateTimeOffset` member reads text with no zone as UTC, so a zoneless `DateTime.Now` would filter hours away on any host outside UTC. Under `DataType.Date`, on `DateTime` and `DateOnly` members, and for any other `Kind`, no zone is written; text values are read as sent.
- **New: `DwCaps.DefaultPageSize`** (off by default) bounds a guarded query that sends no page — `MaxPageSize` only ever bounded a caller who had asked for one.
- **New: `[DwForceWhere(..., AllowNull = true)]`** injects `(field op value OR field IS NULL)` in a group of its own, so a caller's `Or` cannot merge with it: the scope for a record that belongs to one tenant or to none. The context value is still required. `AllowNull` with `IsNull` / `IsNotNull` is refused on the attribute, through `ForcedPredicate` and in a stored rule. On a member that can never be null only the attribute is refused; a rule there, written without the type to hand, injects the comparison alone. The startup check now reports a refused attribute along with every other malformed `[DwForceWhere]`. Stored rules carry it as `forced.allowNull`.
- **New: `[DwEntity(DefaultOrder = "CreatedAt desc, Id")]`** is the order a guarded query takes when its caller sends none — through the `Filter` and `Segment` terminals, the composable `Filter` and `FilterDynamic`, and `Page` on a source nothing has ordered or projected. The caller's own orders win, an already-ordered or projected query keeps its order, and a field this caller may not order by — or, in a segment, may not use in one — is left out and recorded in the trace, never refused. An audited field the default keeps is recorded as a use, as a caller's own order is; a field left out is not. Unguarded calls ignore it, and a `[DwEntity]` on a derived type replaces its base type's, so repeat `DefaultOrder` and `RequirePolicy` there.
- **New: `DwPolicyOptions.AuditRefusals`** (off by default) writes every refused guarded query to the caller's audit buffer, drained to `IDwAuditSink` like a `[DwAudit]` event, so a caller probing for columns leaves a record. `DwAuditEvent` gains `ErrorCode`, and the event names the field by its canonical path — under the strict tier too, although the caller's refusal said `"*"` — cut to 256 characters with control, format, line separator and paragraph separator characters escaped, so an invented name cannot forge a log line or reverse the text after it.
- **Fixed:** a healthy policy store nobody wrote to refused every guarded query fifteen minutes after its last write; the composable `Group` on a guarded query returned the small groups the k-anonymity floor suppresses; it and the composable `Summary` handed back the floor's own count column; a forced null check built with `ForcedPredicate.FromContext`, in code or in a stored rule, failed every guarded query on its type, and is now refused where it is built; and every invalid field name a caller sent kept an access record in the reflection cache for the life of the process, so unique invented names grew memory without limit.

## Version 3.0.0 highlights

- **New: field-level policies.** A layer that decides what each caller may filter, sort, select, group, aggregate and see — attributes for the compile-time half, an optional store for the runtime half. See [above](#field-level-policies).
- **New: three companion packages.** `Policies.Redis` and `Policies.EntityFrameworkCore` hold rules; `Policies.AspNetCore` mounts the admin API, explain, simulate and health, and refuses to map without a named authorization policy.
- **No API breaks.** The 2.x API is untouched and nothing enforces until you opt in. `FilterResult<T>` and `SummaryResult` each gain one nullable `Policy` property, null when the query was not guarded. Two things to know: the package takes three new `Microsoft.Extensions.*` dependencies, and `PolicyException` derives from `LogicException`, so an existing `catch (LogicException)` now also receives policy refusals.
- **Worth knowing before you turn it on:** gating costs nothing measurable, but transforming every row of a large result costs about 1.6x in time and 7x in allocations, because each value is rebuilt after materialization rather than in SQL. `MinGroupSize` ships **on at 5**, so a guarded summary suppresses groups under five until you say otherwise — see [Security](https://doc.dynamicwhere.com/docs/policies/security) and [Configuration](https://doc.dynamicwhere.com/docs/policies/configuration).

## Version 2.1.5 highlights

- **Fixed: the XML documentation shipped with the package.** It drives IntelliSense in your IDE, and three defects degraded it — an unescaped generic argument in the `Select<T>` comment truncated its remarks and returns text, three `ToList` / `ToListAsync` overloads were missing the `getQueryString` description, and `CacheReporting.GetQuickHealthSummary` documented a parameter it does not take. The library now builds with zero warnings. No API or behaviour changes.

## Version 2.1.4 highlights

**Security and correctness fix — upgrade recommended for everyone.**

- **Fixed: values carrying a backslash or a double quote broke the query.** Condition values are embedded in the generated dynamic LINQ expression as string literals, and were not escaped. A search term ending in `\` — the reported case was an Arabic term typed into a search box — escaped its own closing quote, so the parser ran on into the rest of the expression and threw `System.Linq.Dynamic.Core.Exceptions.ParseException: ')' or ',' expected`. Values are now escaped and matched literally, `\` and `"` included, across every `Text` and `Enum` operator.
- **Fixed: a crafted value could rewrite the predicate.** The same missing escape let a value close its literal and append clauses of its own — `x") || (1==1) || Name.Contains("y` turned a `Contains` filter into an always-true predicate and returned every row. Values can no longer break out of their literal.
- **Fixed: `AggregateBy.Alias` could inject extra projection columns.** The alias was only checked for dots, so `"Total, 1 as Leaked"` appended a term to the generated `Select`. Aliases must now be plain identifiers — a leading letter or underscore, then letters, digits, or underscores, with non-Latin letters allowed. Anything else already failed to parse, so nothing that worked is rejected; malformed aliases now throw `AggregationMustHasValidAlias` at validation time.

## Version 2.1.3 highlights

- **MIT licensed.** Free forever for commercial and personal use, no license acceptance required.

## Version 2.1.2 highlights

- **Fixed: ordering across collection navigations.** `OrderBy.Field = "Tags.Value"` on a `List<Tag>` threw `No property or field 'Value' exists in type 'List\`1'`. Collection segments are now reduced to a single comparable value — `Min` ascending, `Max` descending — at any nesting depth. See [Nested navigation](#nested-navigation).

## Version 2.1.0 highlights

- **Heterogeneous `Condition.Values`** — `List<object>` with type-safe coercion. Send raw numbers and booleans without quoting. JSON callers are unaffected; C# code assigning a `List<string>` no longer compiles.
- **Five tuned cache presets** — pick `ForHighMemoryEnvironment`, `ForLowMemoryEnvironment`, `ForDevelopment`, `ForHighFrequencyAccess`, `ForTemporalAccess`, or the default `new CacheOptions()`.
- **Official documentation site** launched at `doc.dynamicwhere.com`.

See **[Breaking Changes & Known Limitations](https://doc.dynamicwhere.com/docs/breaking-changes)** for the complete migration / caveat list.

---

## Compatibility

- **.NET:** 6, 7, 8, 9, 10
- **EF Core providers:** SQL Server, PostgreSQL (Npgsql), MySQL (Pomelo), SQLite — anything that supports `ToQueryString()` for the optional `getQueryString: true` flag.
- **Enum storage:** either. `DataType.Enum` matches by member name (any case) or by number, and translates against an `int` column as readily as a `string` one. What it does not do is the string operators: `Contains` and friends throw against an enum-typed member, so a `string` column that merely holds enum names wants `DataType.Text`.
- **Case-insensitive operators:** emit `.ToLower()` on both sides. Works well on SQL Server's default collation; watch for case-sensitive PostgreSQL `C` locale.

---

## Links

- **Documentation:** [doc.dynamicwhere.com](https://doc.dynamicwhere.com)
- **NuGet:** [nuget.org/packages/DynamicWhere.ex](https://www.nuget.org/packages/DynamicWhere.ex)
- **Source:** [github.com/Sajadh92/DynamicWhere.ex](https://github.com/Sajadh92/DynamicWhere.ex)
- **Issues:** [github.com/Sajadh92/DynamicWhere.ex/issues](https://github.com/Sajadh92/DynamicWhere.ex/issues)

---

## License

[MIT](https://github.com/Sajadh92/DynamicWhere.ex/blob/master/LICENSE) — **Free Forever.** Copyright © 2023-2026 Sajjad H. Al-Khafaji.

Free for commercial and personal use, forever. No license acceptance required, no attribution beyond keeping the copyright notice, no restrictions on redistribution.
